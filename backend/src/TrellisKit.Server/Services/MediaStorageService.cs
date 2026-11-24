namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Moves, copies, and deletes the stored objects of one media item: originals (kept until the member erases them),
    /// served outputs (images media/{id}/, transcoded video/{id}/), public copies (public/media/{id}/,
    /// public/video/{id}/), and retained outputs (removed/{id}/media/ and removed/{id}/video/).
    /// Thread safety: safe to call concurrently for different media items.
    /// </summary>
    public class MediaStorageService
    {
        #region Private-Members

        private readonly IntegrationSet _Integrations;
        private readonly AppLogger _Logger;
        private readonly string _PropertyId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="integrations">Integrations (the object store is read on each call).</param>
        /// <param name="propertyId">Property id: the first segment of every object key.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MediaStorageService(IntegrationSet integrations, string propertyId, AppLogger logger)
        {
            _PropertyId = propertyId ?? throw new ArgumentNullException(nameof(propertyId));
            _Integrations = integrations ?? throw new ArgumentNullException(nameof(integrations));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Delete every object of a media item, including the original and any open multipart upload.
        /// </summary>
        /// <param name="media">Media.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The media is null.</exception>
        public async Task DeleteAllAsync(MediaAttachment media, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            IObjectStore store = _Integrations.ObjectStore;
            if (!String.IsNullOrEmpty(media.MultipartUploadId))
            {
                await store.AbortMultipartAsync(media.SourceLocation, media.SourceKey, media.MultipartUploadId, token).ConfigureAwait(false);
            }
            if (!String.IsNullOrEmpty(media.SourceKey)) await store.DeleteAsync(media.SourceLocation, media.SourceKey, token).ConfigureAwait(false);
            await DeleteOutputsAsync(media, token).ConfigureAwait(false);
            await store.DeletePrefixAsync(MediaLocationEnum.Retained, MediaKeys.RetainedPrefix(_PropertyId, media.Id), token).ConfigureAwait(false);
            await store.DeletePrefixAsync(MediaLocationEnum.Source, MediaKeys.PrivatePrefix(_PropertyId, media.Id), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete the served outputs and public copies of a media item (the original is kept).
        /// </summary>
        /// <param name="media">Media.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The media is null.</exception>
        public async Task DeleteOutputsAsync(MediaAttachment media, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            IObjectStore store = _Integrations.ObjectStore;
            await store.DeletePrefixAsync(MediaLocationEnum.Images, MediaKeys.ImagePrefix(_PropertyId, media.Id), token).ConfigureAwait(false);
            await store.DeletePrefixAsync(MediaLocationEnum.Transcoded, MediaKeys.VideoPrefix(_PropertyId, media.Id), token).ConfigureAwait(false);
            if (media.IsPrivate) await store.DeletePrefixAsync(MediaLocationEnum.Source, MediaKeys.PrivatePrefix(_PropertyId, media.Id), token).ConfigureAwait(false);
            await DeletePublicAsync(media, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete the public copies of a media item.
        /// </summary>
        /// <param name="media">Media.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The media is null.</exception>
        public async Task DeletePublicAsync(MediaAttachment media, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            IObjectStore store = _Integrations.ObjectStore;
            await store.DeletePrefixAsync(MediaLocationEnum.Images, MediaKeys.PublicImagePrefix(_PropertyId, media.Id), token).ConfigureAwait(false);
            await store.DeletePrefixAsync(MediaLocationEnum.Transcoded, MediaKeys.PublicVideoPrefix(_PropertyId, media.Id), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Copy the served outputs to the public paths.
        /// </summary>
        /// <param name="media">Media.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Objects copied.</returns>
        /// <exception cref="ArgumentNullException">The media is null.</exception>
        public async Task<int> CopyToPublicAsync(MediaAttachment media, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            int count = await CopyPrefixAsync(MediaLocationEnum.Images, MediaKeys.ImagePrefix(_PropertyId, media.Id), MediaLocationEnum.Images, MediaKeys.PublicImagePrefix(_PropertyId, media.Id), false, token).ConfigureAwait(false);
            count += await CopyPrefixAsync(MediaLocationEnum.Transcoded, MediaKeys.VideoPrefix(_PropertyId, media.Id), MediaLocationEnum.Transcoded, MediaKeys.PublicVideoPrefix(_PropertyId, media.Id), false, token).ConfigureAwait(false);
            return count;
        }

        /// <summary>
        /// Move the served outputs to the retained location (removed/{id}/media/ and removed/{id}/video/) and delete
        /// the public copies.
        /// </summary>
        /// <param name="media">Media.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Objects moved.</returns>
        /// <exception cref="ArgumentNullException">The media is null.</exception>
        public async Task<int> MoveToRetainedAsync(MediaAttachment media, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            int count = await CopyPrefixAsync(MediaLocationEnum.Images, MediaKeys.ImagePrefix(_PropertyId, media.Id), MediaLocationEnum.Retained, MediaKeys.RetainedImagePrefix(_PropertyId, media.Id), true, token).ConfigureAwait(false);
            count += await CopyPrefixAsync(MediaLocationEnum.Transcoded, MediaKeys.VideoPrefix(_PropertyId, media.Id), MediaLocationEnum.Retained, MediaKeys.RetainedVideoPrefix(_PropertyId, media.Id), true, token).ConfigureAwait(false);
            await DeletePublicAsync(media, token).ConfigureAwait(false);
            return count;
        }

        /// <summary>
        /// Move retained outputs back to the served locations.
        /// </summary>
        /// <param name="media">Media.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Objects moved.</returns>
        /// <exception cref="ArgumentNullException">The media is null.</exception>
        public async Task<int> RestoreFromRetainedAsync(MediaAttachment media, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            int count = await CopyPrefixAsync(MediaLocationEnum.Retained, MediaKeys.RetainedImagePrefix(_PropertyId, media.Id), MediaLocationEnum.Images, MediaKeys.ImagePrefix(_PropertyId, media.Id), true, token).ConfigureAwait(false);
            count += await CopyPrefixAsync(MediaLocationEnum.Retained, MediaKeys.RetainedVideoPrefix(_PropertyId, media.Id), MediaLocationEnum.Transcoded, MediaKeys.VideoPrefix(_PropertyId, media.Id), true, token).ConfigureAwait(false);
            return count;
        }

        /// <summary>
        /// Move the served outputs of a private media item (ticket or direct message attachment) out of every location the
        /// media CDN and the grant cookie serve, into private/{id}/media/ and private/{id}/video/ of the source location
        /// (never served directly), and delete any public copies. Private files are read only through the per-object
        /// token route.
        /// </summary>
        /// <param name="media">Media.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Objects moved.</returns>
        /// <exception cref="ArgumentNullException">The media is null.</exception>
        public async Task<int> MoveToPrivateAsync(MediaAttachment media, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            int count = await CopyPrefixAsync(MediaLocationEnum.Images, MediaKeys.ImagePrefix(_PropertyId, media.Id), MediaLocationEnum.Source, MediaKeys.PrivateImagePrefix(_PropertyId, media.Id), true, token).ConfigureAwait(false);
            count += await CopyPrefixAsync(MediaLocationEnum.Transcoded, MediaKeys.VideoPrefix(_PropertyId, media.Id), MediaLocationEnum.Source, MediaKeys.PrivateVideoPrefix(_PropertyId, media.Id), true, token).ConfigureAwait(false);
            await DeletePublicAsync(media, token).ConfigureAwait(false);
            return count;
        }

        #endregion

        #region Private-Methods

        private async Task<int> CopyPrefixAsync(MediaLocationEnum fromLocation, string fromPrefix, MediaLocationEnum toLocation, string toPrefix, bool move, CancellationToken token)
        {
            IObjectStore store = _Integrations.ObjectStore;
            List<string> keys = await store.ListKeysAsync(fromLocation, fromPrefix, token).ConfigureAwait(false);
            int count = 0;
            foreach (string key in keys)
            {
                string target = toPrefix + key.Substring(fromPrefix.Length);
                try
                {
                    if (move) await store.MoveAsync(fromLocation, key, toLocation, target, token).ConfigureAwait(false);
                    else await store.CopyAsync(fromLocation, key, toLocation, target, token).ConfigureAwait(false);
                    count++;
                }
                catch (FileNotFoundException)
                {
                    _Logger.Debug("[MediaStorageService] " + key + " vanished during " + (move ? "move" : "copy"));
                }
            }
            return count;
        }

        #endregion
    }
}
