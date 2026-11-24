namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One moderation queue row (contract section 10.2): one reported target.
    /// </summary>
    public class ModerationQueueItem : IMetadataPayload
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
        /// FeedPost state, Comment state, or for members Active or Deleted.
        /// </summary>
        public string TargetState { get; set; } = String.Empty;

        /// <summary>
        /// Preview.
        /// </summary>
        public ModerationPreview Preview { get; set; } = new ModerationPreview();

        /// <summary>
        /// Author of the target (the member for User targets).
        /// </summary>
        public UserSummary Author { get; set; } = new UserSummary();

        /// <summary>
        /// Reports in the row.
        /// </summary>
        public long ReportCount { get; set; } = 0;

        /// <summary>
        /// Distinct member reporters.
        /// </summary>
        public long ReporterCount { get; set; } = 0;

        /// <summary>
        /// Reports per reason (bounded by the reason set).
        /// </summary>
        public Dictionary<string, long> Reasons { get; set; } = new Dictionary<string, long>();

        /// <summary>
        /// First report (UTC).
        /// </summary>
        public DateTime FirstReportedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last report (UTC).
        /// </summary>
        public DateTime LastReportedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Review target: first report plus moderation.reviewTargetHours (UTC).
        /// </summary>
        public DateTime DueUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Open and past dueUtc.
        /// </summary>
        public bool IsOverdue { get; set; } = false;

        /// <summary>
        /// Open or Resolved.
        /// </summary>
        public string Status { get; set; } = "Open";

        /// <summary>
        /// Scope of the reports (Feed, or a forum).
        /// </summary>
        public ModerationScopeView? Scope { get; set; } = null;

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
