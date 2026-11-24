namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Bounded history preview of a flag (at most 10 revisions).
    /// </summary>
    public class FlagHistoryPreview
    {
        /// <summary>
        /// Newest published revisions.
        /// </summary>
        public List<FeatureFlagView> Objects { get; set; } = new List<FeatureFlagView>();

        /// <summary>
        /// Published revisions.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// Enumeration path.
        /// </summary>
        public string Enumerate { get; set; } = String.Empty;
    }
}
