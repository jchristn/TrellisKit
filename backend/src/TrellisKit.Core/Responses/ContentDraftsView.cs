namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/app/drafts.
    /// </summary>
    public class ContentDraftsView
    {
        /// <summary>
        /// Current content version (send it as expectedVersion).
        /// </summary>
        public long ContentVersion { get; set; } = 0;

        /// <summary>
        /// At most 100 pending drafts.
        /// </summary>
        public List<FeatureFlagView> Objects { get; set; } = new List<FeatureFlagView>();

        /// <summary>
        /// Pending drafts.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// Enumeration path.
        /// </summary>
        public string Enumerate { get; set; } = String.Empty;

        /// <summary>
        /// Pending drafts per content type: flags, strings, banners, assets.
        /// </summary>
        public Dictionary<string, long> Counts { get; set; } = new Dictionary<string, long>(StringComparer.Ordinal);
    }
}
