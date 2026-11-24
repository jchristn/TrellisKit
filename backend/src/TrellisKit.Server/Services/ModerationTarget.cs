namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// A loaded moderation target: a feed post, a comment, a thread, an assistant message, or a member, with its author
    /// (empty for assistant replies, which no member wrote).
    /// </summary>
    public class ModerationTarget
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
        /// Author of the content (the member for User targets).
        /// </summary>
        public string AuthorUserId { get; set; } = String.Empty;

        /// <summary>
        /// The post, for FeedPost targets.
        /// </summary>
        public FeedPost? Post { get; set; } = null;

        /// <summary>
        /// The comment, for Comment targets.
        /// </summary>
        public Comment? Comment { get; set; } = null;

        /// <summary>
        /// The member, for User targets.
        /// </summary>
        public User? User { get; set; } = null;

        /// <summary>
        /// The thread, for Thread targets (Phase 3).
        /// </summary>
        public ForumThread? Thread { get; set; } = null;

        /// <summary>
        /// The forum of a thread, or of a comment on a thread; null for feed content and members.
        /// </summary>
        public string? ForumId { get; set; } = null;

        /// <summary>
        /// The assistant of an AssistantMessage target (its reports use scope Assistant with this id); null otherwise.
        /// </summary>
        public string? AssistantId { get; set; } = null;

        /// <summary>
        /// Moderation state of a content target provider object (Venue, Instructor, Event, Series, Lesson). Providers
        /// set it in <see cref="ContentTargetProvider.LoadModerationTargetAsync"/>; ignored for posts, comments, and threads.
        /// </summary>
        public ModerationStateEnum State { get; set; } = ModerationStateEnum.None;

        /// <summary>
        /// Whether a provider object is deleted (providers set it; ignored for posts, comments, and threads).
        /// </summary>
        public bool Deleted { get; set; } = false;

        /// <summary>
        /// Whether a provider object's content was purged (providers set it).
        /// </summary>
        public bool Purged { get; set; } = false;

        /// <summary>
        /// Moderation and report scope type of a provider object or of a comment under one (Directory, Events,
        /// Library), or null for feed, forum, and assistant content.
        /// </summary>
        public string? ScopeType { get; set; } = null;

        /// <summary>
        /// The provider type hosting a comment target (Event, Lesson), or null when the comment is on a feed post or a
        /// thread.
        /// </summary>
        public TargetTypeEnum? HostType { get; set; } = null;

        /// <summary>
        /// Title of a provider object (queue preview, notifications), or null.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Plain excerpt of a provider object (queue preview, notifications), or null.
        /// </summary>
        public string? Excerpt { get; set; } = null;

        /// <summary>
        /// Whether a provider object carries media (queue preview).
        /// </summary>
        public bool HasMedia { get; set; } = false;

        /// <summary>
        /// The provider's own payload for the object (for example the loaded row), or null. Never serialized.
        /// </summary>
        public object? Data { get; set; } = null;

        /// <summary>
        /// Report scope id set by a moderation extension, or null.
        /// </summary>
        public string? ScopeId { get; set; } = null;

        /// <summary>
        /// Moderation state of an extension target (for example an idea), or null.
        /// </summary>
        public ModerationStateEnum? ExtensionState { get; set; } = null;

        /// <summary>
        /// Whether an extension target is deleted.
        /// </summary>
        public bool ExtensionDeleted { get; set; } = false;

        /// <summary>
        /// Client path of an extension target or of a comment in an extension conversation, or null.
        /// </summary>
        public string? ExtensionPath { get; set; } = null;

        /// <summary>
        /// Noun of an extension target in notifications (for example idea), or null.
        /// </summary>
        public string? ExtensionNoun { get; set; } = null;

        /// <summary>
        /// Excerpt or title of an extension target, or null.
        /// </summary>
        public string? ExtensionExcerpt { get; set; } = null;

        /// <summary>
        /// Whether reports on the target never auto-hide it (direct messages: the reporter can block).
        /// </summary>
        public bool SkipAutoHide { get; set; } = false;

        /// <summary>
        /// Called after a new report on the target is stored (for example to write a report snapshot), or null.
        /// </summary>
        public Func<Report, System.Threading.CancellationToken, System.Threading.Tasks.Task>? AfterReport { get; set; } = null;

        /// <summary>
        /// Current moderation state of a post or comment (None for members).
        /// </summary>
        public ModerationStateEnum ModerationState
        {
            get { return Post?.ModerationState ?? Comment?.ModerationState ?? Thread?.ModerationState ?? ExtensionState ?? State; }
        }

        /// <summary>
        /// Whether the post or comment is deleted (or the member account deleted).
        /// </summary>
        public bool IsDeleted
        {
            get { return Post?.DeletedUtc != null || Comment?.DeletedUtc != null || Thread?.DeletedUtc != null || (User != null && User.IsDeleted) || Deleted || ExtensionDeleted; }
        }

        /// <summary>
        /// Whether the post or comment content was purged.
        /// </summary>
        public bool IsPurged
        {
            get { return Post?.PurgedUtc != null || Comment?.PurgedUtc != null || Thread?.PurgedUtc != null || Purged; }
        }

        /// <summary>
        /// The post a comment belongs to (the post itself for FeedPost targets), or null.
        /// </summary>
        public string? FeedPostId
        {
            get
            {
                if (Post != null) return Post.Id;
                if (Comment != null && Comment.TargetType == TargetTypeEnum.FeedPost) return Comment.TargetId;
                return null;
            }
        }

        #endregion
    }
}
