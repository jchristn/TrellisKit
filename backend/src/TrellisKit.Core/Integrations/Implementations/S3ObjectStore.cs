namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon;
    using Amazon.S3;
    using Amazon.S3.Model;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// S3 mode object store: one bucket per location from the media.*Bucket settings, region media.s3Region,
    /// credentials from the default AWS credential chain (the ECS task role in production). Upload URLs are real
    /// presigned URLs (SigV4). Every call records trelliskit.integration.* with service s3.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class S3ObjectStore : IObjectStore, IDisposable
    {
        #region Public-Members

        /// <inheritdoc />
        public MediaStorageModeEnum Mode
        {
            get { return MediaStorageModeEnum.S3; }
        }

        #endregion

        #region Private-Members

        private const string _Service = "s3";
        private readonly MediaSettings _Settings;
        private readonly Lazy<IAmazonS3> _Client;
        private readonly bool _OwnsClient;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the store with a client for media.s3Region (created on first use) and the default credential chain.
        /// </summary>
        /// <param name="settings">Media settings (bucket names are read on each call).</param>
        /// <exception cref="ArgumentNullException">The settings are null.</exception>
        public S3ObjectStore(MediaSettings settings)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Client = new Lazy<IAmazonS3>(() => new AmazonS3Client(RegionEndpoint.GetBySystemName(_Settings.S3Region)), LazyThreadSafetyMode.ExecutionAndPublication);
            _OwnsClient = true;
        }

        /// <summary>
        /// Instantiate the store with a supplied client (tests, custom endpoints).
        /// </summary>
        /// <param name="settings">Media settings.</param>
        /// <param name="client">S3 client. Not disposed by this store.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public S3ObjectStore(MediaSettings settings, IAmazonS3 client)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            ArgumentNullException.ThrowIfNull(client);
            _Client = new Lazy<IAmazonS3>(() => client);
            _OwnsClient = false;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public void RequireConfigured(MediaLocationEnum location)
        {
            Bucket(location);
        }

        /// <inheritdoc />
        public string LocationName(MediaLocationEnum location)
        {
            switch (location)
            {
                case MediaLocationEnum.Source:
                    return _Settings.SourceBucket;
                case MediaLocationEnum.VideoSource:
                    return _Settings.VideoSourceBucket;
                case MediaLocationEnum.Images:
                    return _Settings.ImagesBucket;
                case MediaLocationEnum.Transcoded:
                    return _Settings.TranscodedBucket;
                default:
                    return _Settings.RetainedBucket;
            }
        }

        /// <inheritdoc />
        public async Task<PresignedUpload> PresignPutAsync(UploadTarget target, DateTime expiresUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            GetPreSignedUrlRequest request = new GetPreSignedUrlRequest
            {
                BucketName = Bucket(target.Location),
                Key = target.Key,
                Verb = HttpVerb.PUT,
                Expires = expiresUtc,
                ContentType = target.ContentType
            };
            string url = await _Client.Value.GetPreSignedURLAsync(request).ConfigureAwait(false);
            PresignedUpload upload = new PresignedUpload { Url = url };
            upload.Headers["Content-Type"] = target.ContentType;
            return upload;
        }

        /// <inheritdoc />
        public Task<string> InitiateMultipartAsync(UploadTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            return CallAsync("CreateMultipartUpload", async () =>
            {
                InitiateMultipartUploadResponse response = await _Client.Value.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
                {
                    BucketName = Bucket(target.Location),
                    Key = target.Key,
                    ContentType = target.ContentType
                }, token).ConfigureAwait(false);
                return response.UploadId;
            });
        }

        /// <inheritdoc />
        public async Task<List<PresignedPart>> PresignPartsAsync(UploadTarget target, string uploadId, IEnumerable<int> partNumbers, DateTime expiresUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(partNumbers);
            if (String.IsNullOrEmpty(uploadId)) throw new ArgumentNullException(nameof(uploadId));
            string bucket = Bucket(target.Location);
            List<PresignedPart> parts = new List<PresignedPart>();
            foreach (int number in partNumbers)
            {
                string url = await _Client.Value.GetPreSignedURLAsync(new GetPreSignedUrlRequest
                {
                    BucketName = bucket,
                    Key = target.Key,
                    Verb = HttpVerb.PUT,
                    Expires = expiresUtc,
                    UploadId = uploadId,
                    PartNumber = number
                }).ConfigureAwait(false);
                parts.Add(new PresignedPart { PartNumber = number, Url = url });
            }
            return parts;
        }

        /// <inheritdoc />
        public Task<List<StoredPart>> ListPartsAsync(MediaLocationEnum location, string key, string uploadId, CancellationToken token = default)
        {
            return CallAsync("ListParts", async () =>
            {
                List<StoredPart> parts = new List<StoredPart>();
                int? marker = null;
                for (int page = 0; page < 20; page++)
                {
                    ListPartsRequest request = new ListPartsRequest { BucketName = Bucket(location), Key = key, UploadId = uploadId, MaxParts = 1000 };
                    if (marker != null) request.PartNumberMarker = marker.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    ListPartsResponse response;
                    try
                    {
                        response = await _Client.Value.ListPartsAsync(request, token).ConfigureAwait(false);
                    }
                    catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
                    {
                        return parts;
                    }
                    foreach (PartDetail detail in response.Parts ?? new List<PartDetail>())
                    {
                        parts.Add(new StoredPart { PartNumber = detail.PartNumber ?? 0, ETag = detail.ETag ?? String.Empty, SizeBytes = detail.Size ?? 0 });
                    }
                    if (response.IsTruncated != true || response.NextPartNumberMarker == null) break;
                    marker = response.NextPartNumberMarker;
                }
                return parts.OrderBy(p => p.PartNumber).ToList();
            });
        }

        /// <inheritdoc />
        public Task CompleteMultipartAsync(MediaLocationEnum location, string key, string uploadId, List<StoredPart> parts, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(parts);
            return CallAsync("CompleteMultipartUpload", async () =>
            {
                CompleteMultipartUploadRequest request = new CompleteMultipartUploadRequest { BucketName = Bucket(location), Key = key, UploadId = uploadId, PartETags = new List<PartETag>() };
                foreach (StoredPart part in parts.OrderBy(p => p.PartNumber)) request.PartETags.Add(new PartETag(part.PartNumber, part.ETag));
                try
                {
                    await _Client.Value.CompleteMultipartUploadAsync(request, token).ConfigureAwait(false);
                }
                catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.BadRequest || e.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new InvalidOperationException("The multipart upload could not be completed: " + e.ErrorCode, e);
                }
                return true;
            });
        }

        /// <inheritdoc />
        public Task AbortMultipartAsync(MediaLocationEnum location, string key, string uploadId, CancellationToken token = default)
        {
            return CallAsync("AbortMultipartUpload", async () =>
            {
                try
                {
                    await _Client.Value.AbortMultipartUploadAsync(new AbortMultipartUploadRequest { BucketName = Bucket(location), Key = key, UploadId = uploadId }, token).ConfigureAwait(false);
                }
                catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    // Already aborted or completed.
                }
                return true;
            });
        }

        /// <inheritdoc />
        public Task<ObjectHead?> HeadAsync(MediaLocationEnum location, string key, CancellationToken token = default)
        {
            return CallAsync("HeadObject", async () =>
            {
                try
                {
                    GetObjectMetadataResponse response = await _Client.Value.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = Bucket(location), Key = key }, token).ConfigureAwait(false);
                    return (ObjectHead?)new ObjectHead
                    {
                        SizeBytes = response.ContentLength,
                        ContentType = response.Headers?.ContentType,
                        ETag = response.ETag,
                        LastModifiedUtc = response.LastModified?.ToUniversalTime() ?? DateTime.UtcNow
                    };
                }
                catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }
            });
        }

        /// <inheritdoc />
        public Task<byte[]> ReadRangeAsync(MediaLocationEnum location, string key, long offset, int length, CancellationToken token = default)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "The offset must be 0 or more.");
            if (length < 1) throw new ArgumentOutOfRangeException(nameof(length), "The length must be 1 or more.");
            return CallAsync("GetObject", async () =>
            {
                try
                {
                    using (GetObjectResponse response = await _Client.Value.GetObjectAsync(new GetObjectRequest { BucketName = Bucket(location), Key = key, ByteRange = new ByteRange(offset, offset + length - 1) }, token).ConfigureAwait(false))
                    using (MemoryStream memory = new MemoryStream())
                    {
                        await response.ResponseStream.CopyToAsync(memory, token).ConfigureAwait(false);
                        return memory.ToArray();
                    }
                }
                catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    return Array.Empty<byte>();
                }
                catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new FileNotFoundException("Object " + key + " does not exist.", key, e);
                }
            });
        }

        /// <inheritdoc />
        public Task<byte[]> ReadAllAsync(MediaLocationEnum location, string key, CancellationToken token = default)
        {
            return CallAsync("GetObject", async () =>
            {
                try
                {
                    using (GetObjectResponse response = await _Client.Value.GetObjectAsync(new GetObjectRequest { BucketName = Bucket(location), Key = key }, token).ConfigureAwait(false))
                    using (MemoryStream memory = new MemoryStream())
                    {
                        await response.ResponseStream.CopyToAsync(memory, token).ConfigureAwait(false);
                        return memory.ToArray();
                    }
                }
                catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new FileNotFoundException("Object " + key + " does not exist.", key, e);
                }
            });
        }

        /// <inheritdoc />
        public Task PutAsync(MediaLocationEnum location, string key, byte[] data, string contentType, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(data);
            return CallAsync("PutObject", async () =>
            {
                using (MemoryStream memory = new MemoryStream(data, false))
                {
                    await _Client.Value.PutObjectAsync(new PutObjectRequest { BucketName = Bucket(location), Key = key, InputStream = memory, ContentType = contentType }, token).ConfigureAwait(false);
                }
                return true;
            });
        }

        /// <inheritdoc />
        public Task PutFileAsync(MediaLocationEnum location, string key, string filePath, string contentType, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            return CallAsync("PutObject", async () =>
            {
                await _Client.Value.PutObjectAsync(new PutObjectRequest { BucketName = Bucket(location), Key = key, FilePath = filePath, ContentType = contentType }, token).ConfigureAwait(false);
                return true;
            });
        }

        /// <inheritdoc />
        public Task DownloadToFileAsync(MediaLocationEnum location, string key, string filePath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            return CallAsync("GetObject", async () =>
            {
                try
                {
                    using (GetObjectResponse response = await _Client.Value.GetObjectAsync(new GetObjectRequest { BucketName = Bucket(location), Key = key }, token).ConfigureAwait(false))
                    using (FileStream output = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        await response.ResponseStream.CopyToAsync(output, token).ConfigureAwait(false);
                    }
                }
                catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new FileNotFoundException("Object " + key + " does not exist.", key, e);
                }
                return true;
            });
        }

        /// <inheritdoc />
        public Task CopyAsync(MediaLocationEnum fromLocation, string fromKey, MediaLocationEnum toLocation, string toKey, CancellationToken token = default)
        {
            return CallAsync("CopyObject", async () =>
            {
                try
                {
                    await _Client.Value.CopyObjectAsync(new CopyObjectRequest
                    {
                        SourceBucket = Bucket(fromLocation),
                        SourceKey = fromKey,
                        DestinationBucket = Bucket(toLocation),
                        DestinationKey = toKey
                    }, token).ConfigureAwait(false);
                }
                catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new FileNotFoundException("Object " + fromKey + " does not exist.", fromKey, e);
                }
                return true;
            });
        }

        /// <inheritdoc />
        public async Task MoveAsync(MediaLocationEnum fromLocation, string fromKey, MediaLocationEnum toLocation, string toKey, CancellationToken token = default)
        {
            await CopyAsync(fromLocation, fromKey, toLocation, toKey, token).ConfigureAwait(false);
            await DeleteAsync(fromLocation, fromKey, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task DeleteAsync(MediaLocationEnum location, string key, CancellationToken token = default)
        {
            return CallAsync("DeleteObject", async () =>
            {
                await _Client.Value.DeleteObjectAsync(new DeleteObjectRequest { BucketName = Bucket(location), Key = key }, token).ConfigureAwait(false);
                return true;
            });
        }

        /// <inheritdoc />
        public async Task<int> DeletePrefixAsync(MediaLocationEnum location, string prefix, CancellationToken token = default)
        {
            List<string> keys = await ListKeysAsync(location, prefix, token).ConfigureAwait(false);
            int deleted = 0;
            for (int i = 0; i < keys.Count; i += 1000)
            {
                List<string> batch = keys.Skip(i).Take(1000).ToList();
                await CallAsync("DeleteObjects", async () =>
                {
                    DeleteObjectsRequest request = new DeleteObjectsRequest { BucketName = Bucket(location), Objects = batch.Select(k => new KeyVersion { Key = k }).ToList() };
                    await _Client.Value.DeleteObjectsAsync(request, token).ConfigureAwait(false);
                    return true;
                }).ConfigureAwait(false);
                deleted += batch.Count;
            }
            return deleted;
        }

        /// <inheritdoc />
        public Task<List<string>> ListKeysAsync(MediaLocationEnum location, string prefix, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(prefix) || !prefix.EndsWith("/", StringComparison.Ordinal)) throw new ArgumentException("The prefix must end with a slash.", nameof(prefix));
            return CallAsync("ListObjectsV2", async () =>
            {
                List<string> keys = new List<string>();
                string? continuation = null;
                for (int page = 0; page < 100; page++)
                {
                    ListObjectsV2Response response = await _Client.Value.ListObjectsV2Async(new ListObjectsV2Request { BucketName = Bucket(location), Prefix = prefix, ContinuationToken = continuation }, token).ConfigureAwait(false);
                    foreach (S3Object item in response.S3Objects ?? new List<S3Object>()) keys.Add(item.Key);
                    if (response.IsTruncated != true || String.IsNullOrEmpty(response.NextContinuationToken)) break;
                    continuation = response.NextContinuationToken;
                }
                return keys;
            });
        }

        /// <summary>
        /// Dispose the client when this store created it.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose resources.
        /// </summary>
        /// <param name="disposing">Whether managed resources are disposed.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            _Disposed = true;
            if (disposing && _OwnsClient && _Client.IsValueCreated) _Client.Value.Dispose();
        }

        #endregion

        #region Private-Methods

        private string Bucket(MediaLocationEnum location)
        {
            string name = LocationName(location);
            if (String.IsNullOrWhiteSpace(name))
                throw new IntegrationNotConfiguredException("S3 media storage needs the " + SettingKey(location) + " setting; it is empty.");
            return name;
        }

        private static string SettingKey(MediaLocationEnum location)
        {
            switch (location)
            {
                case MediaLocationEnum.Source:
                    return "media.sourceBucket";
                case MediaLocationEnum.VideoSource:
                    return "media.videoSourceBucket";
                case MediaLocationEnum.Images:
                    return "media.imagesBucket";
                case MediaLocationEnum.Transcoded:
                    return "media.transcodedBucket";
                default:
                    return "media.retainedBucket";
            }
        }

        private static async Task<T> CallAsync<T>(string operation, Func<Task<T>> call)
        {
            Activity? activity = TrellisKitTelemetry.StartIntegrationSpan(_Service, operation);
            Stopwatch stopwatch = Stopwatch.StartNew();
            string outcome = "failure";
            try
            {
                T result = await call().ConfigureAwait(false);
                outcome = "success";
                return result;
            }
            finally
            {
                TrellisKitTelemetry.RecordIntegration(activity, _Service, operation, stopwatch.Elapsed, outcome);
                activity?.Dispose();
            }
        }

        #endregion
    }
}
