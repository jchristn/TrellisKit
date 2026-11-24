namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Typed filters of the banner enumeration.
    /// </summary>
    public class BannerFilter
    {
        /// <summary>
        /// Published, not archived, and inside its schedule now, or null.
        /// </summary>
        public bool? Active { get; set; } = null;

        /// <summary>
        /// Placement, or null.
        /// </summary>
        public string? Placement { get; set; } = null;

        /// <summary>
        /// Style, or null.
        /// </summary>
        public string? Style { get; set; } = null;

        /// <summary>
        /// State, or null.
        /// </summary>
        public FeatureFlagStateEnum? State { get; set; } = null;

        /// <summary>
        /// Whether a draft is pending, or null.
        /// </summary>
        public bool? HasDraftChanges { get; set; } = null;

        /// <summary>
        /// Clock for the active filter.
        /// </summary>
        public DateTime NowUtc { get; set; } = DateTime.UtcNow;
    }
}
