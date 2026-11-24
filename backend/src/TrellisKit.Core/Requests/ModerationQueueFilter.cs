namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Typed conditions of the moderation queue enumeration.
    /// </summary>
    public class ModerationQueueFilter
    {
        #region Public-Members

        /// <summary>
        /// False for Open rows (targets with Open reports), true for Resolved rows (targets whose reports are all resolved).
        /// </summary>
        public bool Resolved { get; set; } = false;

        /// <summary>
        /// Target type, or null.
        /// </summary>
        public TargetTypeEnum? TargetType { get; set; } = null;

        /// <summary>
        /// Rows with at least one report of this reason, or null.
        /// </summary>
        public ReportReasonEnum? Reason { get; set; } = null;

        /// <summary>
        /// Rows past (true) or within (false) their review target, or null.
        /// </summary>
        public bool? Overdue { get; set; } = null;

        /// <summary>
        /// Visible scope types, or null for every scope.
        /// </summary>
        public List<string>? ScopeTypes { get; set; } = null;

        /// <summary>
        /// Only reports with this scope type (Feed or Forum), or null.
        /// </summary>
        public string? ScopeTypeFilter { get; set; } = null;

        /// <summary>
        /// Only Forum-scope reports of these forums (empty means every forum).
        /// </summary>
        public List<string> ForumIds { get; set; } = new List<string>();

        /// <summary>
        /// Current time.
        /// </summary>
        public DateTime NowUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
