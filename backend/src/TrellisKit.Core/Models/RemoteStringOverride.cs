namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A remote string override revision (table remotestrings, docs/LIVE_UPDATES_CONTRACT.md section 14.3).
    /// </summary>
    public class RemoteStringOverride
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
        /// Catalog key, for example web.common.retry.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Override text, or null for a revision that removes the override.
        /// </summary>
        public string? Value { get; set; } = null;

        /// <summary>
        /// Editor notes, or null.
        /// </summary>
        public string? Notes { get; set; } = null;

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
