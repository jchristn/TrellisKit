namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Media object storage. One store serves every purpose; each call names its <see cref="MediaLocationEnum"/>
    /// (one bucket per purpose in S3 mode, one folder per purpose under storage.directory in Local mode). Keys use
    /// forward slashes and the flat layout of contract sections 4.0 and 4.8.
    /// Thread safety: implementations are safe to call concurrently.
    /// </summary>
    public interface IObjectStore
    {
        /// <summary>
        /// Storage mode of this store.
        /// </summary>
        MediaStorageModeEnum Mode { get; }

        /// <summary>
        /// Throw when a location is not configured (S3 mode with an empty bucket setting).
        /// </summary>
        /// <param name="location">Location.</param>
        /// <exception cref="IntegrationNotConfiguredException">The location is not configured.</exception>
        void RequireConfigured(MediaLocationEnum location);

        /// <summary>
        /// Bucket name (S3) or folder name (Local) of a location.
        /// </summary>
        /// <param name="location">Location.</param>
        /// <returns>Name, possibly empty when not configured.</returns>
        string LocationName(MediaLocationEnum location);

        /// <summary>
        /// Presign a single PUT of the whole object.
        /// </summary>
        /// <param name="target">Upload target.</param>
        /// <param name="expiresUtc">URL expiry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>URL and the headers the client must send.</returns>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        /// <exception cref="IntegrationNotConfiguredException">The location is not configured.</exception>
        Task<PresignedUpload> PresignPutAsync(UploadTarget target, DateTime expiresUtc, CancellationToken token = default);

        /// <summary>
        /// Start a multipart upload.
        /// </summary>
        /// <param name="target">Upload target.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Upload identifier.</returns>
        /// <exception cref="ArgumentNullException">The target is null.</exception>
        /// <exception cref="IntegrationNotConfiguredException">The location is not configured.</exception>
        Task<string> InitiateMultipartAsync(UploadTarget target, CancellationToken token = default);

        /// <summary>
        /// Presign part PUTs of a multipart upload.
        /// </summary>
        /// <param name="target">Upload target.</param>
        /// <param name="uploadId">Upload identifier.</param>
        /// <param name="partNumbers">One-based part numbers.</param>
        /// <param name="expiresUtc">URL expiry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One URL per part, in the order given.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<List<PresignedPart>> PresignPartsAsync(UploadTarget target, string uploadId, IEnumerable<int> partNumbers, DateTime expiresUtc, CancellationToken token = default);

        /// <summary>
        /// List the parts storage already has for a multipart upload, ordered by part number. Bounded by the
        /// session's part count (at most 1000).
        /// </summary>
        /// <param name="location">Location.</param>
        /// <param name="key">Object key.</param>
        /// <param name="uploadId">Upload identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Parts.</returns>
        Task<List<StoredPart>> ListPartsAsync(MediaLocationEnum location, string key, string uploadId, CancellationToken token = default);

        /// <summary>
        /// Complete a multipart upload from the given parts (in part number order).
        /// </summary>
        /// <param name="location">Location.</param>
        /// <param name="key">Object key.</param>
        /// <param name="uploadId">Upload identifier.</param>
        /// <param name="parts">Parts with their ETags.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="InvalidOperationException">A part is missing or its ETag does not match.</exception>
        Task CompleteMultipartAsync(MediaLocationEnum location, string key, string uploadId, List<StoredPart> parts, CancellationToken token = default);

        /// <summary>
        /// Abort a multipart upload and delete its parts. Missing uploads are ignored.
        /// </summary>
        /// <param name="location">Location.</param>
        /// <param name="key">Object key.</param>
        /// <param name="uploadId">Upload identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task AbortMultipartAsync(MediaLocationEnum location, string key, string uploadId, CancellationToken token = default);

        /// <summary>
        /// Metadata of an object.
        /// </summary>
        /// <param name="location">Location.</param>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metadata, or null when the object does not exist.</returns>
        Task<ObjectHead?> HeadAsync(MediaLocationEnum location, string key, CancellationToken token = default);

        /// <summary>
        /// Read a byte range. Reading past the end returns fewer bytes.
        /// </summary>
        /// <param name="location">Location.</param>
        /// <param name="key">Object key.</param>
        /// <param name="offset">Start offset.</param>
        /// <param name="length">Bytes to read. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Bytes.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The offset or length is out of range.</exception>
        /// <exception cref="System.IO.FileNotFoundException">The object does not exist.</exception>
        Task<byte[]> ReadRangeAsync(MediaLocationEnum location, string key, long offset, int length, CancellationToken token = default);

        /// <summary>
        /// Read a whole object into memory (images only; videos are streamed with <see cref="DownloadToFileAsync"/>).
        /// </summary>
        /// <param name="location">Location.</param>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Bytes.</returns>
        /// <exception cref="System.IO.FileNotFoundException">The object does not exist.</exception>
        Task<byte[]> ReadAllAsync(MediaLocationEnum location, string key, CancellationToken token = default);

        /// <summary>
        /// Write an object.
        /// </summary>
        /// <param name="location">Location.</param>
        /// <param name="key">Object key.</param>
        /// <param name="data">Bytes.</param>
        /// <param name="contentType">Content type.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The data is null.</exception>
        Task PutAsync(MediaLocationEnum location, string key, byte[] data, string contentType, CancellationToken token = default);

        /// <summary>
        /// Write an object from a local file.
        /// </summary>
        /// <param name="location">Location.</param>
        /// <param name="key">Object key.</param>
        /// <param name="filePath">Local file.</param>
        /// <param name="contentType">Content type.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task PutFileAsync(MediaLocationEnum location, string key, string filePath, string contentType, CancellationToken token = default);

        /// <summary>
        /// Copy an object to a local file.
        /// </summary>
        /// <param name="location">Location.</param>
        /// <param name="key">Object key.</param>
        /// <param name="filePath">Destination file (overwritten).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.IO.FileNotFoundException">The object does not exist.</exception>
        Task DownloadToFileAsync(MediaLocationEnum location, string key, string filePath, CancellationToken token = default);

        /// <summary>
        /// Copy an object, possibly between locations.
        /// </summary>
        /// <param name="fromLocation">Source location.</param>
        /// <param name="fromKey">Source key.</param>
        /// <param name="toLocation">Destination location.</param>
        /// <param name="toKey">Destination key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.IO.FileNotFoundException">The source does not exist.</exception>
        Task CopyAsync(MediaLocationEnum fromLocation, string fromKey, MediaLocationEnum toLocation, string toKey, CancellationToken token = default);

        /// <summary>
        /// Move an object (copy, then delete the source).
        /// </summary>
        /// <param name="fromLocation">Source location.</param>
        /// <param name="fromKey">Source key.</param>
        /// <param name="toLocation">Destination location.</param>
        /// <param name="toKey">Destination key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="System.IO.FileNotFoundException">The source does not exist.</exception>
        Task MoveAsync(MediaLocationEnum fromLocation, string fromKey, MediaLocationEnum toLocation, string toKey, CancellationToken token = default);

        /// <summary>
        /// Delete an object. Missing objects are ignored.
        /// </summary>
        /// <param name="location">Location.</param>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteAsync(MediaLocationEnum location, string key, CancellationToken token = default);

        /// <summary>
        /// Delete every object under a prefix (which must end with a slash).
        /// </summary>
        /// <param name="location">Location.</param>
        /// <param name="prefix">Key prefix, for example media/med_x/.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Objects deleted.</returns>
        /// <exception cref="ArgumentException">The prefix is empty or does not end with a slash.</exception>
        Task<int> DeletePrefixAsync(MediaLocationEnum location, string prefix, CancellationToken token = default);

        /// <summary>
        /// List the keys under a prefix (one media item's flat folder, so bounded by its outputs).
        /// </summary>
        /// <param name="location">Location.</param>
        /// <param name="prefix">Key prefix ending with a slash.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Full keys.</returns>
        /// <exception cref="ArgumentException">The prefix is empty or does not end with a slash.</exception>
        Task<List<string>> ListKeysAsync(MediaLocationEnum location, string prefix, CancellationToken token = default);
    }
}
