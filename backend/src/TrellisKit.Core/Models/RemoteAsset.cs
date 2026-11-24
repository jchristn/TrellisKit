namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A remote content asset revision (table remoteassets, docs/LIVE_UPDATES_CONTRACT.md section 14.2).
    /// </summary>
    public class RemoteAsset
    {
        /// <summary>
        /// Revision identifier.
        /// </summary>
        public string RevisionId { get; set; } = String.Empty;

        /// <summary>
        /// Object identifier, stable across revisions.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Asset key clients reference.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Stored content type.
        /// </summary>
        public string ContentType { get; set; } = "image/webp";

        /// <summary>
        /// Stored size.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Width in pixels, or null before upload.
        /// </summary>
        public int? Width { get; set; } = null;

        /// <summary>
        /// Height in pixels, or null before upload.
        /// </summary>
        public int? Height { get; set; } = null;

        /// <summary>
        /// SHA-256 hex of the stored bytes, or null before upload.
        /// </summary>
        public string? Sha256Hex { get; set; } = null;

        /// <summary>
        /// SHA-256 hex of the dark variant, or null.
        /// </summary>
        public string? DarkSha256Hex { get; set; } = null;

        /// <summary>
        /// Dark variant width, or null.
        /// </summary>
        public int? DarkWidth { get; set; } = null;

        /// <summary>
        /// Dark variant height, or null.
        /// </summary>
        public int? DarkHeight { get; set; } = null;

        /// <summary>
        /// Dark variant size, or null.
        /// </summary>
        public long? DarkSizeBytes { get; set; } = null;

        /// <summary>
        /// Alternative text, or null.
        /// </summary>
        public string? AltText { get; set; } = null;

        /// <summary>
        /// State: Draft (never published), Published, or Archived (removed at the next publish).
        /// </summary>
        public FeatureFlagStateEnum State { get; set; } = FeatureFlagStateEnum.Draft;

        /// <summary>
        /// Content version the revision became current in, or null for a draft.
        /// </summary>
        public long? ValidFromVersion { get; set; } = null;

        /// <summary>
        /// Content version the revision stopped being current in, or null.
        /// </summary>
        public long? ValidToVersion { get; set; } = null;

        /// <summary>
        /// Whether the revision is the pending draft.
        /// </summary>
        public bool IsDraft { get; set; } = true;

        /// <summary>
        /// Member who wrote the revision, or null.
        /// </summary>
        public string? EditedByUserId { get; set; } = null;

        /// <summary>
        /// Object creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Revision time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
