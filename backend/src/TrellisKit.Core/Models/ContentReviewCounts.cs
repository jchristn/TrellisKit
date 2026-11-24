namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Counts of the content review queue of one property. Not thread safe.
    /// </summary>
    public class ContentReviewCounts
    {
        #region Public-Members

        /// <summary>
        /// Rows waiting for the reviewer.
        /// </summary>
        public long Pending { get; set; } = 0;

        /// <summary>
        /// Rows flagged and waiting for an administrator.
        /// </summary>
        public long Flagged { get; set; } = 0;

        /// <summary>
        /// Rows flagged since the requested time.
        /// </summary>
        public long FlaggedSince { get; set; } = 0;

        /// <summary>
        /// Rows still Pending after at least one failed review attempt.
        /// </summary>
        public long Failing { get; set; } = 0;

        /// <summary>
        /// When the oldest Pending row was written, or null.
        /// </summary>
        public DateTime? OldestPendingUtc { get; set; } = null;

        #endregion
    }
}
