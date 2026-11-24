namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A catalog key with its remote override (docs/LIVE_UPDATES_CONTRACT.md section 14.3).
    /// </summary>
    public class RemoteStringView
    {
        /// <summary>
        /// Catalog key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Client type: web or mobile.
        /// </summary>
        public string Client { get; set; } = String.Empty;

        /// <summary>
        /// Built-in text.
        /// </summary>
        public string DefaultValue { get; set; } = String.Empty;

        /// <summary>
        /// Tokens the text may use.
        /// </summary>
        public List<string> Tokens { get; set; } = new List<string>();

        /// <summary>
        /// Longest override.
        /// </summary>
        public int MaxLength { get; set; } = 200;

        /// <summary>
        /// Override id (rst_), or null when not overridden.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// The draft value when a draft is pending, else the published override, or null.
        /// </summary>
        public string? Value { get; set; } = null;

        /// <summary>
        /// The published override, or null.
        /// </summary>
        public string? PublishedValue { get; set; } = null;

        /// <summary>
        /// Editor notes, or null.
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// Whether a published override exists.
        /// </summary>
        public bool Overridden { get; set; } = false;

        /// <summary>
        /// Whether a draft removes the published override at the next publish.
        /// </summary>
        public bool PendingDelete { get; set; } = false;

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
        /// Latest override edit, or null.
        /// </summary>
        public DateTime? UpdatedUtc { get; set; } = null;
    }
}
