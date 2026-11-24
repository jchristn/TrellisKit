namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Fields shared by Thread and ThreadSummary (docs/PHASE3_CONTRACT.md sections 2.2 and 2.3).
    /// </summary>
    public class ThreadCoreView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (thr_).
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
        /// Original or LinkedFeedPost.
        /// </summary>
        public ThreadKindEnum Kind { get; set; } = ThreadKindEnum.Original;

        /// <summary>
        /// Thread type.
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
        /// Plain-text excerpt.
        /// </summary>
        public string Excerpt { get; set; } = String.Empty;

        /// <summary>
        /// Who may read it.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// State.
        /// </summary>
        public ThreadStateEnum State { get; set; } = ThreadStateEnum.Published;

        /// <summary>
        /// Whether pinned.
        /// </summary>
        public bool IsPinned { get; set; } = false;

        /// <summary>
        /// Pin time.
        /// </summary>
        public DateTime? PinnedUtc { get; set; } = null;

        /// <summary>
        /// Whether locked.
        /// </summary>
        public bool IsLocked { get; set; } = false;

        /// <summary>
        /// Lock time.
        /// </summary>
        public DateTime? LockedUtc { get; set; } = null;

        /// <summary>
        /// Lock reason.
        /// </summary>
        public string? LockReason { get; set; } = null;

        /// <summary>
        /// Conversation target (comments, reactions, and reports go here).
        /// </summary>
        public ContentTarget CommentTarget
        {
            get { return _CommentTarget; }
            set { _CommentTarget = value ?? new ContentTarget(); }
        }

        /// <summary>
        /// Source of a LinkedFeedPost thread, or null.
        /// </summary>
        public ThreadSourceView? Source { get; set; } = null;

        /// <summary>
        /// Active feed card, or null.
        /// </summary>
        public ThreadPromotionView? Promotion { get; set; } = null;

        /// <summary>
        /// Visible comments of the conversation target.
        /// </summary>
        public long CommentCount { get; set; } = 0;

        /// <summary>
        /// Distinct commenters other than the author.
        /// </summary>
        public int UniqueCommenterCount { get; set; } = 0;

        /// <summary>
        /// Reaction counts of the conversation target.
        /// </summary>
        public Dictionary<string, long> ReactionCounts
        {
            get { return _ReactionCounts; }
            set { _ReactionCounts = value ?? new Dictionary<string, long>(); }
        }

        /// <summary>
        /// Reaction total of the conversation target.
        /// </summary>
        public long ReactionTotal { get; set; } = 0;

        /// <summary>
        /// The caller's reactions.
        /// </summary>
        public List<string> MyReactions
        {
            get { return _MyReactions; }
            set { _MyReactions = value ?? new List<string>(); }
        }

        /// <summary>
        /// Trending score.
        /// </summary>
        public double TrendingScore { get; set; } = 0;

        /// <summary>
        /// Last activity.
        /// </summary>
        public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last visible comment.
        /// </summary>
        public DateTime? LastCommentUtc { get; set; } = null;

        /// <summary>
        /// Whether the caller follows the thread.
        /// </summary>
        public bool IsFollowed { get; set; } = false;

        /// <summary>
        /// Whether the thread has activity since the caller last read it.
        /// </summary>
        public bool IsUnread { get; set; } = false;

        /// <summary>
        /// Last edit outside the grace period.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        /// <summary>
        /// Stored revisions.
        /// </summary>
        public int RevisionCount { get; set; } = 0;

        /// <summary>
        /// Moderation state for the author and moderators in scope, or null.
        /// </summary>
        public ModerationInfo? Moderation { get; set; } = null;

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
        /// What the caller may do.
        /// </summary>
        public ThreadViewer Viewer
        {
            get { return _Viewer; }
            set { _Viewer = value ?? new ThreadViewer(); }
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
        private List<string> _MyReactions = new List<string>();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();
        private ThreadViewer _Viewer = new ThreadViewer();

        #endregion
    }
}
