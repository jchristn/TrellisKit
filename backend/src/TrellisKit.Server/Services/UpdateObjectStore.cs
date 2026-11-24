namespace TrellisKit.Server.Services
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Live update storage over the engine object store abstraction (docs/LIVE_UPDATES_CONTRACT.md section 17.1).
    /// S3 mode uses the updates bucket (keys mirror the CDN paths: updates/assets/{propertyId}/{sha256}); Local mode
    /// keeps the same keys under {storage.directory}/updates. Source maps live under private/sourcemaps/, outside the
    /// publicly served updates/ prefix, and are never served.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class UpdateObjectStore
    {
        #region Public-Members

        /// <summary>
        /// Storage mode chosen at startup (updates.storageMode is a Restart setting).
        /// </summary>
        public MediaStorageModeEnum Mode { get; }

        /// <summary>
        /// Underlying object store.
        /// </summary>
        public IObjectStore Store { get; }

        #endregion

        #region Private-Members

        private const MediaLocationEnum _Location = MediaLocationEnum.Source;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the store from the running settings.
        /// </summary>
        /// <param name="settings">Running settings.</param>
        /// <exception cref="ArgumentNullException">The settings are null.</exception>
        public UpdateObjectStore(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            Mode = settings.Updates.StorageMode;
            if (Mode == MediaStorageModeEnum.S3)
            {
                string region = String.IsNullOrWhiteSpace(settings.Updates.S3Region) ? settings.Media.S3Region : settings.Updates.S3Region;
                Store = new S3ObjectStore(new MediaSettings { StorageMode = MediaStorageModeEnum.S3, S3Region = region, SourceBucket = settings.Updates.Bucket });
            }
            else
            {
                StorageSettings local = new StorageSettings { Directory = Path.Combine(settings.Storage.Directory, "updates") };
                Store = new LocalObjectStore(local, new LocalUploadSigner(() => settings.Auth.TokenEncryptionKey));
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Object key of a code update asset.
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <param name="sha256Hex">SHA-256 hex.</param>
        /// <returns>Key.</returns>
        public static string AssetKey(string propertyId, string sha256Hex)
        {
            return "updates/assets/" + propertyId + "/" + sha256Hex;
        }

        /// <summary>
        /// Object key of a private source map.
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <param name="sha256Hex">SHA-256 hex.</param>
        /// <returns>Key.</returns>
        public static string SourceMapKey(string propertyId, string sha256Hex)
        {
            return "private/sourcemaps/" + propertyId + "/" + sha256Hex;
        }

        /// <summary>
        /// Head an object.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Head, or null when missing.</returns>
        public Task<ObjectHead?> HeadAsync(string key, CancellationToken token = default)
        {
            return Store.HeadAsync(_Location, key, token);
        }

        /// <summary>
        /// Store a verified file under a key.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="filePath">Local file.</param>
        /// <param name="contentType">Content type.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task PutFileAsync(string key, string filePath, string contentType, CancellationToken token = default)
        {
            return Store.PutFileAsync(_Location, key, filePath, contentType, token);
        }

        /// <summary>
        /// Read a byte range.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="offset">Offset.</param>
        /// <param name="length">Length.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Bytes.</returns>
        public Task<byte[]> ReadRangeAsync(string key, long offset, int length, CancellationToken token = default)
        {
            return Store.ReadRangeAsync(_Location, key, offset, length, token);
        }

        /// <summary>
        /// Delete an object.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task DeleteAsync(string key, CancellationToken token = default)
        {
            return Store.DeleteAsync(_Location, key, token);
        }

        #endregion
    }
}
