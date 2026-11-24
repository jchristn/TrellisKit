namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One report claimed by the moderation due worker (overduenotifiedutc set by this pass).
    /// </summary>
    public class ModerationOverdueClaim
    {
        #region Public-Members

        /// <summary>
        /// Report identifier.
        /// </summary>
        public string ReportId { get; set; } = String.Empty;

        /// <summary>
        /// Target type.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.FeedPost;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Scope type.
        /// </summary>
        public string ScopeType { get; set; } = "Feed";

        /// <summary>
        /// Scope identifier (a forum id for Forum scope, * for the Feed).
        /// </summary>
        public string? ScopeId { get; set; } = null;

        #endregion
    }
}
