namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A feed post row (contract sections 5 and 15). State is derived from the timestamps and the moderation state, see <see cref="StateAt"/>.
    /// </summary>
    public class FeedPost
    {
        #region Public-Members

        /// <summary>
        /// Identifier (fpo_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Author.
        /// </summary>
        public string AuthorUserId { get; set; } = String.Empty;

        /// <summary>
        /// Kind (Original in Phase 2).
        /// </summary>
        public FeedPostKindEnum Kind { get; set; } = FeedPostKindEnum.Original;

        /// <summary>
        /// Source type for promoted content (Phase 3), or null.
        /// </summary>
        public string? SourceType { get; set; } = null;

        /// <summary>
        /// Source identifier for promoted content (Phase 3), or null.
        /// </summary>
        public string? SourceId { get; set; } = null;

        /// <summary>
        /// Optional title (up to 140 characters).
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Normalized Markdown body.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Plain-text excerpt (200 characters).
        /// </summary>
        public string Excerpt { get; set; } = String.Empty;

        /// <summary>
        /// Visibility (Members or Public).
        /// </summary>
        public FeedVisibilityEnum Visibility { get; set; } = FeedVisibilityEnum.Members;

        /// <summary>
        /// Audience (Public, SignedIn, Members); Public matches visibility Public.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Cached link preview row, or null.
        /// </summary>
        public string? LinkPreviewId { get; set; } = null;

        /// <summary>
        /// Whether the author wants a preview card.
        /// </summary>
        public bool ShowLinkPreview { get; set; } = true;

        /// <summary>
        /// Whether comments are allowed.
        /// </summary>
        public bool CommentsEnabled { get; set; } = true;

        /// <summary>
        /// Scheduled publish time, or null.
        /// </summary>
        public DateTime? PublishAtUtc { get; set; } = null;

        /// <summary>
        /// When the post first became visible, or null.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <summary>
        /// Whether the post is pinned.
        /// </summary>
        public bool IsPinned { get; set; } = false;

        /// <summary>
        /// When it was pinned.
        /// </summary>
        public DateTime? PinnedUtc { get; set; } = null;

        /// <summary>
        /// Who pinned it.
        /// </summary>
        public string? PinnedByUserId { get; set; } = null;

        /// <summary>
        /// Last edit after the grace period, or null.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        /// <summary>
        /// Stored revisions.
        /// </summary>
        public int RevisionCount { get; set; } = 0;

        /// <summary>
        /// Attached media.
        /// </summary>
        public int AttachmentCount { get; set; } = 0;

        /// <summary>
        /// Whether a video is attached.
        /// </summary>
        public bool HasVideo { get; set; } = false;

        /// <summary>
        /// Visible comments.
        /// </summary>
        public int CommentCount { get; set; } = 0;

        /// <summary>
        /// Reactions of every stored key.
        /// </summary>
        public int ReactionTotal { get; set; } = 0;

        /// <summary>
        /// Open reports (maintained from the reports table).
        /// </summary>
        public int OpenReportCount { get; set; } = 0;

        /// <summary>
        /// Moderation state.
        /// </summary>
        public ModerationStateEnum ModerationState { get; set; } = ModerationStateEnum.None;

        /// <summary>
        /// When the moderation state last changed.
        /// </summary>
        public DateTime? ModeratedUtc { get; set; } = null;

        /// <summary>
        /// Moderator, or null for the system.
        /// </summary>
        public string? ModeratedByUserId { get; set; } = null;

        /// <summary>
        /// Moderation reason.
        /// </summary>
        public string? ModerationReason { get; set; } = null;

        /// <summary>
        /// Soft delete time, or null.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Who deleted it.
        /// </summary>
        public string? DeletedByUserId { get; set; } = null;

        /// <summary>
        /// When retained data was purged, or null.
        /// </summary>
        public DateTime? PurgedUtc { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The discussion thread opened from this post in a forum, or null.
        /// </summary>
        public string? DiscussionThreadId { get; set; } = null;

        /// <summary>
        /// Who opened the discussion thread, or null.
        /// </summary>
        public string? DiscussionLinkedByUserId { get; set; } = null;

        /// <summary>
        /// Whether the source of a PromotedLesson or PromotedEvent card is not live (hidden, removed, unpublished, or
        /// canceled): the card then leaves the feed until the source is live again (docs/PHASE4_CONTRACT.md section 5.8).
        /// </summary>
        public bool SourceHidden { get; set; } = false;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The post's state at a time: Deleted, then Hidden or Removed (moderation), then Published (publishedUtc set),
        /// then Scheduled (publishAtUtc in the future), otherwise Processing (waiting for attachments).
        /// </summary>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <returns>State.</returns>
        public FeedPostStateEnum StateAt(DateTime nowUtc)
        {
            if (DeletedUtc != null) return FeedPostStateEnum.Deleted;
            if (ModerationState == ModerationStateEnum.Hidden) return FeedPostStateEnum.Hidden;
            if (ModerationState == ModerationStateEnum.Removed) return FeedPostStateEnum.Removed;
            if (PublishedUtc != null) return FeedPostStateEnum.Published;
            if (PublishAtUtc != null && PublishAtUtc.Value > nowUtc) return FeedPostStateEnum.Scheduled;
            return FeedPostStateEnum.Processing;
        }

        #endregion
    }
}
