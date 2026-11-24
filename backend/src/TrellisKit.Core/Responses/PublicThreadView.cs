namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A public sample thread (docs/PHASE3_CONTRACT.md section 10).
    /// </summary>
    public class PublicThreadView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Forum.
        /// </summary>
        public ForumRefView Forum
        {
            get { return _Forum; }
            set { _Forum = value ?? new ForumRefView(); }
        }

        /// <summary>
        /// Kind.
        /// </summary>
        public ThreadKindEnum Kind { get; set; } = ThreadKindEnum.Original;

        /// <summary>
        /// Type.
        /// </summary>
        public ThreadTypeEnum Type { get; set; } = ThreadTypeEnum.Discussion;

        /// <summary>
        /// Author.
        /// </summary>
        public UserSummary Author
        {
            get { return _Author; }
            set { _Author = value ?? new UserSummary(); }
        }

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Body (mentions are plain text).
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Excerpt.
        /// </summary>
        public string Excerpt { get; set; } = String.Empty;

        /// <summary>
        /// Audience.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Public;

        /// <summary>
        /// State.
        /// </summary>
        public ThreadStateEnum State { get; set; } = ThreadStateEnum.Published;

        /// <summary>
        /// Whether pinned.
        /// </summary>
        public bool IsPinned { get; set; } = false;

        /// <summary>
        /// Whether locked.
        /// </summary>
        public bool IsLocked { get; set; } = false;

        /// <summary>
        /// Whether an accepted answer exists.
        /// </summary>
        public bool HasAcceptedAnswer { get; set; } = false;

        /// <summary>
        /// Poll state, or null.
        /// </summary>
        public PublicPollSummaryView? Poll { get; set; } = null;

        /// <summary>
        /// Feedback status, or null.
        /// </summary>
        public FeedbackSummaryView? Feedback { get; set; } = null;

        /// <summary>
        /// Conversation target.
        /// </summary>
        public ContentTarget CommentTarget
        {
            get { return _CommentTarget; }
            set { _CommentTarget = value ?? new ContentTarget(); }
        }

        /// <summary>
        /// Visible comments.
        /// </summary>
        public long CommentCount { get; set; } = 0;

        /// <summary>
        /// Distinct commenters.
        /// </summary>
        public int UniqueCommenterCount { get; set; } = 0;

        /// <summary>
        /// Reaction counts.
        /// </summary>
        public Dictionary<string, long> ReactionCounts
        {
            get { return _ReactionCounts; }
            set { _ReactionCounts = value ?? new Dictionary<string, long>(); }
        }

        /// <summary>
        /// Reaction total.
        /// </summary>
        public long ReactionTotal { get; set; } = 0;

        /// <summary>
        /// Trending score.
        /// </summary>
        public double TrendingScore { get; set; } = 0;

        /// <summary>
        /// Last activity.
        /// </summary>
        public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// At most the first 4 attachments (public paths).
        /// </summary>
        public List<MediaView> AttachmentPreview
        {
            get { return _AttachmentPreview; }
            set { _AttachmentPreview = value ?? new List<MediaView>(); }
        }

        /// <summary>
        /// Attachments.
        /// </summary>
        public int AttachmentCount { get; set; } = 0;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        /// <summary>
        /// Publish time.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private ForumRefView _Forum = new ForumRefView();
        private UserSummary _Author = new UserSummary();
        private ContentTarget _CommentTarget = new ContentTarget();
        private Dictionary<string, long> _ReactionCounts = new Dictionary<string, long>();
        private List<MediaView> _AttachmentPreview = new List<MediaView>();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
