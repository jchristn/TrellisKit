namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One row of GET /api/v1/admin/feed (contract section 10.1).
    /// </summary>
    public class AdminFeedItem
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Kind.
        /// </summary>
        public FeedPostKindEnum Kind { get; set; } = FeedPostKindEnum.Original;

        /// <summary>
        /// Title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Excerpt.
        /// </summary>
        public string Excerpt { get; set; } = String.Empty;

        /// <summary>
        /// Author.
        /// </summary>
        public UserSummary Author { get; set; } = new UserSummary();

        /// <summary>
        /// State (including Deleted).
        /// </summary>
        public FeedPostStateEnum State { get; set; } = FeedPostStateEnum.Published;

        /// <summary>
        /// Visibility.
        /// </summary>
        public FeedVisibilityEnum Visibility { get; set; } = FeedVisibilityEnum.Members;

        /// <summary>
        /// Audience.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Pinned.
        /// </summary>
        public bool IsPinned { get; set; } = false;

        /// <summary>
        /// Scheduled time.
        /// </summary>
        public DateTime? PublishAtUtc { get; set; } = null;

        /// <summary>
        /// Published time.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Edited.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        /// <summary>
        /// Deleted.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Attachments.
        /// </summary>
        public int AttachmentCount { get; set; } = 0;

        /// <summary>
        /// Has a video.
        /// </summary>
        public bool HasVideo { get; set; } = false;

        /// <summary>
        /// Visible comments.
        /// </summary>
        public int CommentCount { get; set; } = 0;

        /// <summary>
        /// Reactions.
        /// </summary>
        public int ReactionTotal { get; set; } = 0;

        /// <summary>
        /// Open reports.
        /// </summary>
        public int OpenReportCount { get; set; } = 0;

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
