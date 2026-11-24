namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// GET /api/v1/admin/moderation/queue/{targetType}/{targetId}: the target in any state with bounded previews (at most 5
    /// each, with the enumerate path) of its reports, moderation actions, and revisions, and the author's history.
    /// </summary>
    public class ModerationQueueDetail : IMetadataPayload
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
        /// FeedPost, Comment, or UserSummary (full content, any state).
        /// </summary>
        public object? Target { get; set; } = null;

        /// <summary>
        /// Deep-link context.
        /// </summary>
        public ModerationContext Context { get; set; } = new ModerationContext();

        /// <summary>
        /// Revisions preview (empty for members).
        /// </summary>
        public BoundedPreview<object> Revisions { get; set; } = new BoundedPreview<object>();

        /// <summary>
        /// Reports preview (newest first).
        /// </summary>
        public BoundedPreview<ReportView> Reports { get; set; } = new BoundedPreview<ReportView>();

        /// <summary>
        /// Moderation actions preview (newest first).
        /// </summary>
        public BoundedPreview<ModerationActionView> Actions { get; set; } = new BoundedPreview<ModerationActionView>();

        /// <summary>
        /// Author history.
        /// </summary>
        public ModerationAuthorHistory AuthorHistory { get; set; } = new ModerationAuthorHistory();

        /// <summary>
        /// Open reports on the target in the caller's scope.
        /// </summary>
        public long OpenReports { get; set; } = 0;

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
