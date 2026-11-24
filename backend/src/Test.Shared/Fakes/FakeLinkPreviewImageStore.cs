namespace Test.Shared.Fakes
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Link preview image store that records stored images and returns fixed dimensions.
    /// </summary>
    public sealed class FakeLinkPreviewImageStore : ILinkPreviewImageStore
    {
        /// <summary>
        /// Stored image bytes by url hash.
        /// </summary>
        public ConcurrentDictionary<string, byte[]> Stored { get; } = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);

        /// <inheritdoc />
        public Task<StoredPreviewImage?> StoreLinkPreviewImageAsync(string urlHash, byte[] data, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(urlHash)) throw new ArgumentNullException(nameof(urlHash));
            ArgumentNullException.ThrowIfNull(data);
            Stored[urlHash] = data;
            return Task.FromResult<StoredPreviewImage?>(new StoredPreviewImage { Key = "link-previews/" + urlHash + "/card.jpg", Width = 1200, Height = 630 });
        }

        /// <inheritdoc />
        public string? LinkPreviewImageUrl(string? key)
        {
            return String.IsNullOrEmpty(key) ? null : "/api/v1/media/files/" + key;
        }
    }
}
