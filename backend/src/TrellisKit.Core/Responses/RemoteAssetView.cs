namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A remote content asset (docs/LIVE_UPDATES_CONTRACT.md section 14.2).
    /// </summary>
    public class RemoteAssetView
    {
        /// <summary>
        /// Asset id (ras_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Key clients reference.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = "image/webp";

        /// <summary>
        /// Size.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Width, or null before upload.
        /// </summary>
        public int? Width { get; set; } = null;

        /// <summary>
        /// Height, or null before upload.
        /// </summary>
        public int? Height { get; set; } = null;

        /// <summary>
        /// SHA-256 hex, or null before upload.
        /// </summary>
        public string? Sha256 { get; set; } = null;

        /// <summary>
        /// Absolute URL, or null before upload.
        /// </summary>
        public string? Url { get; set; } = null;

        /// <summary>
        /// Alternative text.
        /// </summary>
        public string? AltText { get; set; } = null;

        /// <summary>
        /// Dark variant, or null.
        /// </summary>
        public RemoteAssetVariantView? DarkVariant { get; set; } = null;

        /// <summary>
        /// State (Draft, Published, Archived), or null.
        /// </summary>
        public FeatureFlagStateEnum? State { get; set; } = null;

        /// <summary>
        /// Whether a draft is pending.
        /// </summary>
        public bool HasDraftChanges { get; set; } = false;

        /// <summary>
        /// Content version of this revision, or null for a draft.
        /// </summary>
        public long? PublishedInVersion { get; set; } = null;

        /// <summary>
        /// ETag for If-Match, or null.
        /// </summary>
        public string? ETag { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Revision time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
