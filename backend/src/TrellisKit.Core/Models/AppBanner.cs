namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A banner (announcement) revision (tables banners and bannerclients; rules in featureflagrules with ownertype Banner).
    /// </summary>
    public class AppBanner
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
        /// Title, 1 to 80 characters.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Markdown body, 0 to 500 characters.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Style: Info, Success, Warning, or Critical.
        /// </summary>
        public string Style { get; set; } = "Info";

        /// <summary>
        /// Placement: Global or a screen.
        /// </summary>
        public string Placement { get; set; } = "Global";

        /// <summary>
        /// Whether members may dismiss it (never for Critical).
        /// </summary>
        public bool Dismissible { get; set; } = true;

        /// <summary>
        /// Priority; higher shows first.
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// Start of the schedule, or null.
        /// </summary>
        public DateTime? StartsUtc { get; set; } = null;

        /// <summary>
        /// End of the schedule, or null.
        /// </summary>
        public DateTime? EndsUtc { get; set; } = null;

        /// <summary>
        /// Link label, or null.
        /// </summary>
        public string? LinkLabel { get; set; } = null;

        /// <summary>
        /// App route of the link, or null.
        /// </summary>
        public string? LinkPath { get; set; } = null;

        /// <summary>
        /// HTTPS URL of the link, or null.
        /// </summary>
        public string? LinkUrl { get; set; } = null;

        /// <summary>
        /// Remote asset id (ras_) of an image, or null.
        /// </summary>
        public string? ImageAssetId { get; set; } = null;

        /// <summary>
        /// Client types that show it (mobile, web).
        /// </summary>
        public List<string> Clients { get; set; } = new List<string>();

        /// <summary>
        /// Targeting rules (the value is ignored; no rules means everyone).
        /// </summary>
        public List<FeatureFlagRule> Rules { get; set; } = new List<FeatureFlagRule>();

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
