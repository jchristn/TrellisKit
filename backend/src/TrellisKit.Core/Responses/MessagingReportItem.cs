namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// One direct message report in the Messaging moderation queue (snapshots only, never the conversation).
    /// </summary>
    public class MessagingReportItem
    {
        #region Public-Members

        /// <summary>
        /// Report identifier.
        /// </summary>
        public string ReportId { get; set; } = String.Empty;

        /// <summary>
        /// Message or Conversation.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.Message;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Reason.
        /// </summary>
        public ReportReasonEnum Reason { get; set; } = ReportReasonEnum.Other;

        /// <summary>
        /// Reporter details, or null.
        /// </summary>
        public string? Details { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public ReportStatusEnum Status { get; set; } = ReportStatusEnum.Open;

        /// <summary>
        /// Reporter.
        /// </summary>
        public UserSummary? Reporter { get; set; } = null;

        /// <summary>
        /// Author of the reported message (or the other member for a conversation).
        /// </summary>
        public UserSummary? TargetAuthor { get; set; } = null;

        /// <summary>
        /// Snapshot identifier, or null.
        /// </summary>
        public string? SnapshotId { get; set; } = null;

        /// <summary>
        /// Report labels.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Report tags.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
