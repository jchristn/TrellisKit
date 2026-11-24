namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A comment row (contract sections 7 and 15).
    /// </summary>
    public class Comment
    {
        #region Public-Members

        /// <summary>
        /// Identifier (cmt_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Target type (FeedPost in Phase 2).
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.FeedPost;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Parent comment, or null for a top-level comment.
        /// </summary>
        public string? ParentCommentId { get; set; } = null;

        /// <summary>
        /// Top-level ancestor (itself for a top-level comment).
        /// </summary>
        public string RootCommentId { get; set; } = String.Empty;

        /// <summary>
        /// The comment the member replied to when the reply was flattened at the maximum depth, or null.
        /// </summary>
        public string? ReplyToCommentId { get; set; } = null;

        /// <summary>
        /// Materialized path: ancestor ids and its own id, each followed by a slash.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Depth (0 for top level).
        /// </summary>
        public int Depth { get; set; } = 0;

        /// <summary>
        /// Author.
        /// </summary>
        public string AuthorUserId { get; set; } = String.Empty;

        /// <summary>
        /// Normalized Markdown body (null once erased).
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Plain-text excerpt.
        /// </summary>
        public string? Excerpt { get; set; } = null;

        /// <summary>
        /// Cached link preview row, or null.
        /// </summary>
        public string? LinkPreviewId { get; set; } = null;

        /// <summary>
        /// Whether the author wants a preview card.
        /// </summary>
        public bool ShowLinkPreview { get; set; } = true;

        /// <summary>
        /// Attached media.
        /// </summary>
        public int AttachmentCount { get; set; } = 0;

        /// <summary>
        /// Visible direct replies.
        /// </summary>
        public int ChildCount { get; set; } = 0;

        /// <summary>
        /// Visible replies at any depth.
        /// </summary>
        public int DescendantCount { get; set; } = 0;

        /// <summary>
        /// Reactions of every stored key.
        /// </summary>
        public int ReactionTotal { get; set; } = 0;

        /// <summary>
        /// reactionTotal + 2 * childCount.
        /// </summary>
        public int Score { get; set; } = 0;

        /// <summary>
        /// Last edit after the grace period, or null.
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
        /// Why it was deleted.
        /// </summary>
        public CommentDeletedReasonEnum? DeletedReason { get; set; } = null;

        /// <summary>
        /// When retained data was purged, or null.
        /// </summary>
        public DateTime? PurgedUtc { get; set; } = null;

        /// <summary>
        /// Whether the comment is the accepted answer of its Question thread.
        /// </summary>
        public bool IsAcceptedAnswer { get; set; } = false;

        /// <summary>
        /// Whether the comment is the chosen review of its Feedback thread.
        /// </summary>
        public bool IsFeedbackReview { get; set; } = false;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
