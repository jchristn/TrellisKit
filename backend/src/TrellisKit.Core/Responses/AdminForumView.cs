namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A forum with admin counters (docs/PHASE3_CONTRACT.md section 3.2).
    /// </summary>
    public class AdminForumView : ForumView
    {
        #region Public-Members

        /// <summary>
        /// Open Forum-scope reports.
        /// </summary>
        public long OpenReportCount { get; set; } = 0;

        /// <summary>
        /// Members whose Moderator assignments cover the forum.
        /// </summary>
        public long ModeratorCount { get; set; } = 0;

        /// <summary>
        /// Pinned threads.
        /// </summary>
        public long PinnedCount { get; set; } = 0;

        /// <summary>
        /// Threads published in the last 30 days.
        /// </summary>
        public long ThreadsLast30d { get; set; } = 0;

        /// <summary>
        /// When the forum was archived, or null.
        /// </summary>
        public DateTime? ArchivedUtc { get; set; } = null;

        #endregion
    }
}
