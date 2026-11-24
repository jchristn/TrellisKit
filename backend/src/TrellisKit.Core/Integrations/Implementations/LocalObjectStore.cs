namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Local mode object store: one folder per location under storage.directory (source/, video-source/, images/,
    /// transcoded/, retained/), with the same keys as S3. Upload URLs point at the backend's signed local upload
    /// routes; multipart parts are kept next to the object in "&lt;key&gt;.parts/" (part files 00001 and so on, each
    /// with an .etag file) until complete concatenates them.
    /// Thread safety: safe to call concurrently; every write goes to a unique temporary file and is moved into place.
    /// </summary>
    public class LocalObjectStore : IObjectStore
    {
        #region Public-Members

        /// <inheritdoc />
        public MediaStorageModeEnum Mode
        {
            get { return MediaStorageModeEnum.Local; }
        }

        /// <summary>
        /// Signer for local upload URLs.
        /// </summary>
        public LocalUploadSigner Signer { get; }

        /// <summary>
        /// Copy buffer size in bytes. Default 81920. Minimum 4096, maximum 4194304.
        /// </summary>
        public int BufferSize
        {
            get { return _BufferSize; }
            set { _BufferSize = Math.Clamp(value, 4096, 4194304); }
        }

        #endregion

        #region Private-Members

        private const string _PartsSuffix = ".parts";
        private readonly StorageSettings _Storage;
        private int _BufferSize = 81920;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the store.
        /// </summary>
        /// <param name="storage">Storage settings (storage.directory is read on each call).</param>
        /// <param name="signer">Local upload URL signer.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public LocalObjectStore(StorageSettings storage, LocalUploadSigner signer)
        {
            _Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            Signer = signer ?? throw new ArgumentNullException(nameof(signer));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public void RequireConfigured(MediaLocationEnum location)
        {
        }

        /// <inheritdoc />
        public string LocationName(MediaLocationEnum location)
        {
            return MediaKeys.FolderName(location);
        }

        /// <inheritdoc />
        public Task<PresignedUpload> PresignPutAsync(UploadTarget target, DateTime expiresUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            PresignedUpload upload = new PresignedUpload { Url = Signer.BuildUrl(target.Origin, LocalUploadSigner.SinglePath(target.MediaId), expiresUtc) };
            upload.Headers["Content-Type"] = target.ContentType;
            return Task.FromResult(upload);
        }

        /// <inheritdoc />
        public Task<string> InitiateMultipartAsync(UploadTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            Directory.CreateDirectory(PartsDirectory(target.Location, target.Key));
            return Task.FromResult("local-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant());
        }

        /// <inheritdoc />
        public Task<List<PresignedPart>> PresignPartsAsync(UploadTarget target, string uploadId, IEnumerable<int> partNumbers, DateTime expiresUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(partNumbers);
            List<PresignedPart> parts = partNumbers.Select(n => new PresignedPart
            {
                PartNumber = n,
                Url = Signer.BuildUrl(target.Origin, LocalUploadSigner.PartPath(target.MediaId, n), expiresUtc)
            }).ToList();
            return Task.FromResult(parts);
        }

        /// <inheritdoc />
        public async Task<List<StoredPart>> ListPartsAsync(MediaLocationEnum location, string key, string uploadId, CancellationToken token = default)
        {
            List<StoredPart> parts = new List<StoredPart>();
            string directory = PartsDirectory(location, key);
            if (!Directory.Exists(directory)) return parts;
            foreach (string file in Directory.GetFiles(directory))
            {
                string name = Path.GetFileName(file);
                if (name.Length != 5 || !Int32.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int number)) continue;
                string etagFile = file + ".etag";
                string etag = File.Exists(etagFile) ? (await File.ReadAllTextAsync(etagFile, token).ConfigureAwait(false)).Trim() : String.Empty;
                parts.Add(new StoredPart { PartNumber = number, ETag = etag, SizeBytes = new FileInfo(file).Length });
            }
            return parts.OrderBy(p => p.PartNumber).ToList();
        }

        /// <inheritdoc />
        public async Task CompleteMultipartAsync(MediaLocationEnum location, string key, string uploadId, List<StoredPart> parts, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(parts);
            List<StoredPart> stored = await ListPartsAsync(location, key, uploadId, token).ConfigureAwait(false);
            Dictionary<int, StoredPart> byNumber = stored.ToDictionary(p => p.PartNumber);
            foreach (StoredPart part in parts)
            {
                if (!byNumber.TryGetValue(part.PartNumber, out StoredPart? found) || !EtagEquals(found.ETag, part.ETag))
                    throw new InvalidOperationException("Part " + part.PartNumber + " is missing or its ETag does not match.");
            }
            string directory = PartsDirectory(location, key);
            string target = ResolvePath(location, key);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (FileStream output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, _BufferSize, true))
            {
                foreach (StoredPart part in parts.OrderBy(p => p.PartNumber))
                {
                    using (FileStream input = new FileStream(Path.Combine(directory, PartName(part.PartNumber)), FileMode.Open, FileAccess.Read, FileShare.Read, _BufferSize, true))
                    {
                        await input.CopyToAsync(output, _BufferSize, token).ConfigureAwait(false);
                    }
                }
            }
            File.Move(temp, target, true);
            TryDeleteDirectory(directory);
        }

        /// <inheritdoc />
        public Task AbortMultipartAsync(MediaLocationEnum location, string key, string uploadId, CancellationToken token = default)
        {
            TryDeleteDirectory(PartsDirectory(location, key));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<ObjectHead?> HeadAsync(MediaLocationEnum location, string key, CancellationToken token = default)
        {
            string path = ResolvePath(location, key);
            if (!File.Exists(path)) return Task.FromResult<ObjectHead?>(null);
            FileInfo info = new FileInfo(path);
            return Task.FromResult<ObjectHead?>(new ObjectHead { SizeBytes = info.Length, LastModifiedUtc = info.LastWriteTimeUtc, ContentType = MediaKeys.ContentTypeFor(path) });
        }

        /// <inheritdoc />
        public async Task<byte[]> ReadRangeAsync(MediaLocationEnum location, string key, long offset, int length, CancellationToken token = default)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "The offset must be 0 or more.");
            if (length < 1) throw new ArgumentOutOfRangeException(nameof(length), "The length must be 1 or more.");
            string path = ResolvePath(location, key);
            if (!File.Exists(path)) throw new FileNotFoundException("Object " + key + " does not exist.", key);
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
            {
                if (offset >= stream.Length) return Array.Empty<byte>();
                stream.Position = offset;
                int toRead = (int)Math.Min(length, stream.Length - offset);
                byte[] buffer = new byte[toRead];
                await stream.ReadExactlyAsync(buffer, 0, toRead, token).ConfigureAwait(false);
                return buffer;
            }
        }

        /// <inheritdoc />
        public async Task<byte[]> ReadAllAsync(MediaLocationEnum location, string key, CancellationToken token = default)
        {
            string path = ResolvePath(location, key);
            if (!File.Exists(path)) throw new FileNotFoundException("Object " + key + " does not exist.", key);
            return await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task PutAsync(MediaLocationEnum location, string key, byte[] data, string contentType, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(data);
            string path = ResolvePath(location, key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temp, data, token).ConfigureAwait(false);
            File.Move(temp, path, true);
        }

        /// <inheritdoc />
        public async Task PutFileAsync(MediaLocationEnum location, string key, string filePath, string contentType, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            string path = ResolvePath(location, key);
            await CopyFileAsync(filePath, path, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DownloadToFileAsync(MediaLocationEnum location, string key, string filePath, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            string path = ResolvePath(location, key);
            if (!File.Exists(path)) throw new FileNotFoundException("Object " + key + " does not exist.", key);
            await CopyFileAsync(path, filePath, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task CopyAsync(MediaLocationEnum fromLocation, string fromKey, MediaLocationEnum toLocation, string toKey, CancellationToken token = default)
        {
            string source = ResolvePath(fromLocation, fromKey);
            if (!File.Exists(source)) throw new FileNotFoundException("Object " + fromKey + " does not exist.", fromKey);
            await CopyFileAsync(source, ResolvePath(toLocation, toKey), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task MoveAsync(MediaLocationEnum fromLocation, string fromKey, MediaLocationEnum toLocation, string toKey, CancellationToken token = default)
        {
            string source = ResolvePath(fromLocation, fromKey);
            if (!File.Exists(source)) throw new FileNotFoundException("Object " + fromKey + " does not exist.", fromKey);
            string target = ResolvePath(toLocation, toKey);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(source, target, true);
            TryDeleteEmptyParent(source, fromLocation);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task DeleteAsync(MediaLocationEnum location, string key, CancellationToken token = default)
        {
            string path = ResolvePath(location, key);
            try
            {
                if (File.Exists(path)) File.Delete(path);
                TryDeleteDirectory(path + _PartsSuffix);
                TryDeleteEmptyParent(path, location);
            }
            catch (IOException)
            {
                // A file in use is retried by the cleanup worker.
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<int> DeletePrefixAsync(MediaLocationEnum location, string prefix, CancellationToken token = default)
        {
            RequirePrefix(prefix);
            string directory = ResolvePath(location, prefix.TrimEnd('/'));
            if (!Directory.Exists(directory)) return Task.FromResult(0);
            int count = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length;
            TryDeleteDirectory(directory);
            TryDeleteEmptyParent(directory, location);
            return Task.FromResult(count);
        }

        /// <inheritdoc />
        public Task<List<string>> ListKeysAsync(MediaLocationEnum location, string prefix, CancellationToken token = default)
        {
            RequirePrefix(prefix);
            List<string> keys = new List<string>();
            string directory = ResolvePath(location, prefix.TrimEnd('/'));
            if (!Directory.Exists(directory)) return Task.FromResult(keys);
            string root = LocationRoot(location);
            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".tmp", StringComparison.Ordinal)) continue;
                keys.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
            }
            keys.Sort(StringComparer.Ordinal);
            return Task.FromResult(keys);
        }

        /// <summary>
        /// Receive a local upload body (single object or one part), streaming it to disk without buffering it whole.
        /// </summary>
        /// <param name="location">Location.</param>
        /// <param name="key">Object key.</param>
        /// <param name="partNumber">Part number, or null for a single upload.</param>
        /// <param name="body">Request body stream.</param>
        /// <param name="maxBytes">Largest accepted body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored object or part with its quoted MD5 ETag and size.</returns>
        /// <exception cref="ArgumentNullException">The body is null.</exception>
        /// <exception cref="InvalidDataException">The body is larger than maxBytes (nothing is kept).</exception>
        public async Task<StoredPart> ReceiveAsync(MediaLocationEnum location, string key, int? partNumber, Stream body, long maxBytes, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(body);
            string target = partNumber == null ? ResolvePath(location, key) : Path.Combine(PartsDirectory(location, key), PartName(partNumber.Value));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            long total = 0;
            string etag;
            try
            {
                using (IncrementalHash md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5))
                using (FileStream output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, _BufferSize, true))
                {
                    byte[] buffer = new byte[_BufferSize];
                    while (true)
                    {
                        int read = await body.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                        if (read <= 0) break;
                        total += read;
                        if (total > maxBytes) throw new InvalidDataException("The body is larger than " + maxBytes + " bytes.");
                        md5.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    }
                    etag = "\"" + Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant() + "\"";
                }
                File.Move(temp, target, true);
            }
            catch
            {
                TryDeleteFile(temp);
                throw;
            }
            if (partNumber != null) await File.WriteAllTextAsync(target + ".etag", etag, token).ConfigureAwait(false);
            return new StoredPart { PartNumber = partNumber ?? 1, ETag = etag, SizeBytes = total };
        }

        /// <summary>
        /// Full path of an existing object, or null when it does not exist or the key is unsafe.
        /// </summary>
        /// <param name="location">Location.</param>
        /// <param name="key">Object key.</param>
        /// <returns>Path, or null.</returns>
        public string? ResolveExisting(MediaLocationEnum location, string key)
        {
            try
            {
                string path = ResolvePath(location, key);
                return File.Exists(path) ? path : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// Full path of a key (the file need not exist). Rejects keys that could escape the location folder.
        /// </summary>
        /// <param name="location">Location.</param>
        /// <param name="key">Object key.</param>
        /// <returns>Path.</returns>
        /// <exception cref="ArgumentException">The key is empty or unsafe.</exception>
        public string ResolvePath(MediaLocationEnum location, string key)
        {
            if (String.IsNullOrWhiteSpace(key)) throw new ArgumentException("The key is empty.", nameof(key));
            string[] segments = key.Split('/');
            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.Contains('\\') || segment.Contains(':'))
                    throw new ArgumentException("The key " + key + " is not a safe object key.", nameof(key));
            }
            string root = LocationRoot(location);
            string path = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new ArgumentException("The key " + key + " escapes its location.", nameof(key));
            return path;
        }

        #endregion

        #region Private-Methods

        private string LocationRoot(MediaLocationEnum location)
        {
            return Path.Combine(Path.GetFullPath(_Storage.Directory), MediaKeys.FolderName(location));
        }

        private string PartsDirectory(MediaLocationEnum location, string key)
        {
            return ResolvePath(location, key) + _PartsSuffix;
        }

        private static string PartName(int partNumber)
        {
            return partNumber.ToString("D5", CultureInfo.InvariantCulture);
        }

        private static bool EtagEquals(string a, string b)
        {
            return String.Equals((a ?? String.Empty).Trim().Trim('"'), (b ?? String.Empty).Trim().Trim('"'), StringComparison.OrdinalIgnoreCase);
        }

        private static void RequirePrefix(string prefix)
        {
            if (String.IsNullOrEmpty(prefix) || !prefix.EndsWith("/", StringComparison.Ordinal)) throw new ArgumentException("The prefix must end with a slash.", nameof(prefix));
        }

        private async Task CopyFileAsync(string source, string target, CancellationToken token)
        {
            string? directory = Path.GetDirectoryName(target);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, _BufferSize, true))
                using (FileStream output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, _BufferSize, true))
                {
                    await input.CopyToAsync(output, _BufferSize, token).ConfigureAwait(false);
                }
                File.Move(temp, target, true);
            }
            catch
            {
                TryDeleteFile(temp);
                throw;
            }
        }

        private void TryDeleteEmptyParent(string path, MediaLocationEnum location)
        {
            try
            {
                string root = LocationRoot(location);
                string? parent = Path.GetDirectoryName(path);
                while (!String.IsNullOrEmpty(parent) && parent.Length > root.Length && parent.StartsWith(root, StringComparison.Ordinal))
                {
                    if (!Directory.Exists(parent) || Directory.EnumerateFileSystemEntries(parent).Any()) return;
                    Directory.Delete(parent);
                    parent = Path.GetDirectoryName(parent);
                }
            }
            catch (IOException)
            {
                // Empty folders are harmless.
            }
            catch (UnauthorizedAccessException)
            {
                // Empty folders are harmless.
            }
        }

        private static void TryDeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (IOException)
            {
                // Retried by the cleanup worker.
            }
            catch (UnauthorizedAccessException)
            {
                // Retried by the cleanup worker.
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // Temporary files are harmless.
            }
        }

        #endregion
    }
}
