namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A banner (docs/LIVE_UPDATES_CONTRACT.md section 14.1).
    /// </summary>
    public class BannerView
    {
        /// <summary>
        /// Banner id (ban_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Markdown body.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Info, Success, Warning, or Critical.
        /// </summary>
        public string Style { get; set; } = "Info";

        /// <summary>
        /// Placement.
        /// </summary>
        public string Placement { get; set; } = "Global";

        /// <summary>
        /// Whether members may dismiss it.
        /// </summary>
        public bool Dismissible { get; set; } = true;

        /// <summary>
        /// Priority; higher first.
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// Schedule start, or null.
        /// </summary>
        public DateTime? StartsUtc { get; set; } = null;

        /// <summary>
        /// Schedule end, or null.
        /// </summary>
        public DateTime? EndsUtc { get; set; } = null;

        /// <summary>
        /// Link, or null.
        /// </summary>
        public BannerLinkView? Link { get; set; } = null;

        /// <summary>
        /// Remote asset id of an image, or null.
        /// </summary>
        public string? ImageAssetId { get; set; } = null;

        /// <summary>
        /// Client types.
        /// </summary>
        public List<string> Clients { get; set; } = new List<string>();

        /// <summary>
        /// Targeting rules (the value is ignored).
        /// </summary>
        public List<FlagRuleView> Rules { get; set; } = new List<FlagRuleView>();

        /// <summary>
        /// Hash of the title and body; a dismissal holds while it is unchanged.
        /// </summary>
        public string ContentHash { get; set; } = String.Empty;

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
