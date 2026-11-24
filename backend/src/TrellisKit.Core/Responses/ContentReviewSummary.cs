namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// GET /api/v1/admin/content-review/summary: queue counts and whether the reviewer can run.
    /// </summary>
    public class ContentReviewSummary
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
        /// Rows flagged in the last 24 hours.
        /// </summary>
        public long FlaggedLast24Hours { get; set; } = 0;

        /// <summary>
        /// Pending rows whose review failed at least once.
        /// </summary>
        public long Failing { get; set; } = 0;

        /// <summary>
        /// When the oldest Pending row was written, or null.
        /// </summary>
        public DateTime? OldestPendingUtc { get; set; } = null;

        /// <summary>
        /// Whether the reviewer can run now (AI on, the Moderation role has executors, the job is enabled).
        /// </summary>
        public bool ReviewerReady { get; set; } = false;

        /// <summary>
        /// Why rows stay Pending (AiOff, NoExecutors, JobDisabled, JobsOff), or null when ready.
        /// </summary>
        public string? ReviewerProblem { get; set; } = null;

        /// <summary>
        /// The problem in words, or null when ready.
        /// </summary>
        public string? ReviewerMessage { get; set; } = null;

        /// <summary>
        /// Status of the latest content.review run, or null.
        /// </summary>
        public string? LastRunStatus { get; set; } = null;

        /// <summary>
        /// When the latest content.review run was created, or null.
        /// </summary>
        public DateTime? LastRunUtc { get; set; } = null;

        /// <summary>
        /// When the next scheduled content.review run is due, or null.
        /// </summary>
        public DateTime? NextRunUtc { get; set; } = null;

        #endregion
    }
}
