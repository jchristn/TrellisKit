namespace TrellisKit.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Stores link preview images through the media pipeline (re-encoded without metadata, long edge at most 1200)
    /// and builds their URLs. Implemented by the media service; the link preview service depends only on this seam.
    /// Thread safety: implementations are safe to call concurrently.
    /// </summary>
    public interface ILinkPreviewImageStore
    {
        /// <summary>
        /// Re-encode and store a fetched preview image.
        /// </summary>
        /// <param name="urlHash">Hash of the normalized page URL (hex), used in the object key.</param>
        /// <param name="data">Fetched image bytes (JPEG, PNG, or WebP).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Stored image, or null when the bytes are not an accepted image or cannot be decoded.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<StoredPreviewImage?> StoreLinkPreviewImageAsync(string urlHash, byte[] data, CancellationToken token = default);

        /// <summary>
        /// URL clients use for a stored preview image (absolute with media.publicBaseUrl, otherwise root-relative).
        /// </summary>
        /// <param name="key">Object key from <see cref="StoreLinkPreviewImageAsync"/>, or null.</param>
        /// <returns>URL, or null when the key is null or empty.</returns>
        string? LinkPreviewImageUrl(string? key);
    }
}
