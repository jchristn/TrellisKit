namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One aggregated moderation queue row: the reports on one target with the requested status.
    /// </summary>
    public class ModerationQueueRow
    {
        #region Public-Members

        /// <summary>
        /// Target type.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.FeedPost;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Author of the target.
        /// </summary>
        public string? TargetAuthorUserId { get; set; } = null;

        /// <summary>
        /// Reports in the row.
        /// </summary>
        public long ReportCount { get; set; } = 0;

        /// <summary>
        /// Distinct member reporters in the row (system reports excluded).
        /// </summary>
        public long ReporterCount { get; set; } = 0;

        /// <summary>
        /// First report (UTC).
        /// </summary>
        public DateTime FirstReportedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last report (UTC).
        /// </summary>
        public DateTime LastReportedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Earliest review target of the row (UTC).
        /// </summary>
        public DateTime DueUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether the row holds Open reports.
        /// </summary>
        public bool HasOpen { get; set; } = false;

        /// <summary>
        /// Scope type of the reports (Feed or Forum).
        /// </summary>
        public string ScopeType { get; set; } = "Feed";

        /// <summary>
        /// Scope identifier (a forum id for Forum scope).
        /// </summary>
        public string? ScopeId { get; set; } = null;

        #endregion
    }
}
