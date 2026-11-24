namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A report on a feed post, comment, or member (contract sections 9.1 and 15). System reports from the word filter
    /// have no reporter and reason Filter.
    /// </summary>
    public class Report
    {
        #region Public-Members

        /// <summary>
        /// Identifier (rpt_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Reporter, or null for a system report.
        /// </summary>
        public string? ReporterUserId { get; set; } = null;

        /// <summary>
        /// Target type (FeedPost, Comment, or User).
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.FeedPost;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Author of the target (the member for User targets).
        /// </summary>
        public string? TargetAuthorUserId { get; set; } = null;

        /// <summary>
        /// Moderation scope type (Feed in Phase 2).
        /// </summary>
        public string ScopeType { get; set; } = "Feed";

        /// <summary>
        /// Moderation scope identifier.
        /// </summary>
        public string ScopeId { get; set; } = "*";

        /// <summary>
        /// Reason.
        /// </summary>
        public ReportReasonEnum Reason { get; set; } = ReportReasonEnum.Other;

        /// <summary>
        /// Details, or null.
        /// </summary>
        public string? Details { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public ReportStatusEnum Status { get; set; } = ReportStatusEnum.Open;

        /// <summary>
        /// Moderator who resolved it, or null.
        /// </summary>
        public string? ResolvedByUserId { get; set; } = null;

        /// <summary>
        /// When it was resolved (UTC), or null.
        /// </summary>
        public DateTime? ResolvedUtc { get; set; } = null;

        /// <summary>
        /// Resolution note, or null.
        /// </summary>
        public string? ResolutionNote { get; set; } = null;

        /// <summary>
        /// Moderation action that resolved it, or null.
        /// </summary>
        public string? ModerationActionId { get; set; } = null;

        /// <summary>
        /// Review target (created plus moderation.reviewTargetHours).
        /// </summary>
        public DateTime DueUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the ModerationOverdue notice was sent, or null.
        /// </summary>
        public DateTime? OverdueNotifiedUtc { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
