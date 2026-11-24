namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A forum thread row (docs/PHASE3_CONTRACT.md sections 2.2 and 17).
    /// </summary>
    public class ForumThread
    {
        #region Public-Members

        /// <summary>
        /// Identifier (thr_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Forum identifier.
        /// </summary>
        public string ForumId { get; set; } = String.Empty;

        /// <summary>
        /// Original or LinkedFeedPost.
        /// </summary>
        public ThreadKindEnum Kind { get; set; } = ThreadKindEnum.Original;

        /// <summary>
        /// Thread type.
        /// </summary>
        public ThreadTypeEnum Type { get; set; } = ThreadTypeEnum.Discussion;

        /// <summary>
        /// Author (the feed post author for LinkedFeedPost threads).
        /// </summary>
        public string AuthorUserId { get; set; } = String.Empty;

        /// <summary>
        /// Title (3 to 140 characters).
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Body in the post Markdown subset (null for LinkedFeedPost threads).
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Plain-text excerpt.
        /// </summary>
        public string Excerpt { get; set; } = String.Empty;

        /// <summary>
        /// Link preview, or null.
        /// </summary>
        public string? LinkPreviewId { get; set; } = null;

        /// <summary>
        /// Whether the link preview is shown.
        /// </summary>
        public bool ShowLinkPreview { get; set; } = true;

        /// <summary>
        /// Who may read the thread (never broader than the forum).
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Source type (FeedPost for LinkedFeedPost threads), or null.
        /// </summary>
        public string? SourceType { get; set; } = null;

        /// <summary>
        /// Source identifier, or null.
        /// </summary>
        public string? SourceId { get; set; } = null;

        /// <summary>
        /// Conversation target type.
        /// </summary>
        public TargetTypeEnum CommentTargetType { get; set; } = TargetTypeEnum.Thread;

        /// <summary>
        /// Conversation target identifier.
        /// </summary>
        public string CommentTargetId { get; set; } = String.Empty;

        /// <summary>
        /// Active feed card, or null.
        /// </summary>
        public string? PromotedFeedPostId { get; set; } = null;

        /// <summary>
        /// Who promoted it, or null.
        /// </summary>
        public string? PromotedByUserId { get; set; } = null;

        /// <summary>
        /// When it was promoted, or null.
        /// </summary>
        public DateTime? PromotedUtc { get; set; } = null;

        /// <summary>
        /// Whether the thread is pinned in its forum.
        /// </summary>
        public bool IsPinned { get; set; } = false;

        /// <summary>
        /// Pin time, or null.
        /// </summary>
        public DateTime? PinnedUtc { get; set; } = null;

        /// <summary>
        /// Who pinned it, or null.
        /// </summary>
        public string? PinnedByUserId { get; set; } = null;

        /// <summary>
        /// Whether the thread is locked (fully frozen).
        /// </summary>
        public bool IsLocked { get; set; } = false;

        /// <summary>
        /// Lock time, or null.
        /// </summary>
        public DateTime? LockedUtc { get; set; } = null;

        /// <summary>
        /// Who locked it, or null.
        /// </summary>
        public string? LockedByUserId { get; set; } = null;

        /// <summary>
        /// Lock reason (0 to 500 characters), or null.
        /// </summary>
        public string? LockReason { get; set; } = null;

        /// <summary>
        /// Accepted answer comment (Question threads), or null.
        /// </summary>
        public string? AcceptedCommentId { get; set; } = null;

        /// <summary>
        /// Whether the accepted comment exists and is visible.
        /// </summary>
        public bool HasAcceptedAnswer { get; set; } = false;

        /// <summary>
        /// Feedback status (Feedback threads), or null.
        /// </summary>
        public FeedbackStatusEnum? FeedbackStatus { get; set; } = null;

        /// <summary>
        /// What the member wants reviewed, or null.
        /// </summary>
        public string? FeedbackFocus { get; set; } = null;

        /// <summary>
        /// Reviewer, or null.
        /// </summary>
        public string? FeedbackReviewerId { get; set; } = null;

        /// <summary>
        /// Review comment, or null.
        /// </summary>
        public string? FeedbackReviewCommentId { get; set; } = null;

        /// <summary>
        /// Last status change, or null.
        /// </summary>
        public DateTime? FeedbackStatusUtc { get; set; } = null;

        /// <summary>
        /// Attached media count.
        /// </summary>
        public int AttachmentCount { get; set; } = 0;

        /// <summary>
        /// Whether a video is attached.
        /// </summary>
        public bool HasVideo { get; set; } = false;

        /// <summary>
        /// Visible comments on the thread's conversation.
        /// </summary>
        public int CommentCount { get; set; } = 0;

        /// <summary>
        /// Distinct authors of visible comments, excluding the thread author.
        /// </summary>
        public int UniqueCommenterCount { get; set; } = 0;

        /// <summary>
        /// Reactions on the thread's conversation.
        /// </summary>
        public int ReactionTotal { get; set; } = 0;

        /// <summary>
        /// Open reports on the thread.
        /// </summary>
        public int OpenReportCount { get; set; } = 0;

        /// <summary>
        /// Trending score (section 5).
        /// </summary>
        public double TrendingScore { get; set; } = 0;

        /// <summary>
        /// When the score was computed, or null.
        /// </summary>
        public DateTime? TrendingComputedUtc { get; set; } = null;

        /// <summary>
        /// Last activity (section 2.2).
        /// </summary>
        public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last visible comment, or null.
        /// </summary>
        public DateTime? LastCommentUtc { get; set; } = null;

        /// <summary>
        /// Last edit outside the grace period, or null.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        /// <summary>
        /// Stored revisions.
        /// </summary>
        public int RevisionCount { get; set; } = 0;

        /// <summary>
        /// Moderation state.
        /// </summary>
        public ModerationStateEnum ModerationState { get; set; } = ModerationStateEnum.None;

        /// <summary>
        /// Moderation time, or null.
        /// </summary>
        public DateTime? ModeratedUtc { get; set; } = null;

        /// <summary>
        /// Moderator, or null (auto-hide).
        /// </summary>
        public string? ModeratedByUserId { get; set; } = null;

        /// <summary>
        /// Moderation reason, or null.
        /// </summary>
        public string? ModerationReason { get; set; } = null;

        /// <summary>
        /// Publish time, or null while Processing.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <summary>
        /// Deletion time, or null.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Who deleted it, or null.
        /// </summary>
        public string? DeletedByUserId { get; set; } = null;

        /// <summary>
        /// Purge time, or null.
        /// </summary>
        public DateTime? PurgedUtc { get; set; } = null;

        /// <summary>
        /// Creation time (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
