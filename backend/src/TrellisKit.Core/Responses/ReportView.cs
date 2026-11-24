namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A report as moderators see it (queue detail and report enumerations).
    /// </summary>
    public class ReportView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Reporter (the Deleted member summary after account deletion), or null for a system report.
        /// </summary>
        public UserSummary? Reporter { get; set; } = null;

        /// <summary>
        /// Target type.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.FeedPost;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Reason.
        /// </summary>
        public ReportReasonEnum Reason { get; set; } = ReportReasonEnum.Other;

        /// <summary>
        /// Details.
        /// </summary>
        public string? Details { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public ReportStatusEnum Status { get; set; } = ReportStatusEnum.Open;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Review target (UTC).
        /// </summary>
        public DateTime DueUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Resolved (UTC).
        /// </summary>
        public DateTime? ResolvedUtc { get; set; } = null;

        /// <summary>
        /// Display name of the resolving moderator ("admin token" for the break-glass token), or null.
        /// </summary>
        public string? ResolvedByName { get; set; } = null;

        /// <summary>
        /// Resolution note.
        /// </summary>
        public string? ResolutionNote { get; set; } = null;

        /// <summary>
        /// Resolving moderation action.
        /// </summary>
        public string? ModerationActionId { get; set; } = null;

        /// <summary>
        /// Labels (never null).
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags (never null).
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
