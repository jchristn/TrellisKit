namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Input to <see cref="NotificationService"/>: one notification for one recipient. The service derives the
    /// category, renders the English title, applies the suppression rules, and stores it.
    /// </summary>
    public class NotificationDraft
    {
        /// <summary>
        /// Recipient member. Required.
        /// </summary>
        public string RecipientUserId { get; set; } = String.Empty;

        /// <summary>
        /// Type. Required.
        /// </summary>
        public NotificationTypeEnum Type { get; set; } = NotificationTypeEnum.PostComment;

        /// <summary>
        /// Member whose action caused it, or null for system notifications (ModerationAction uses null or the moderator;
        /// the title never names the moderator).
        /// </summary>
        public string? ActorUserId { get; set; } = null;

        /// <summary>
        /// Target type (FeedPost, Comment, User, ...). Required.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.FeedPost;

        /// <summary>
        /// Target identifier. Required.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Context: the feed post, or null.
        /// </summary>
        public string? FeedPostId { get; set; } = null;

        /// <summary>
        /// Context: the comment, or null.
        /// </summary>
        public string? CommentId { get; set; } = null;

        /// <summary>
        /// Context: the parent of the comment, or null.
        /// </summary>
        public string? ParentCommentId { get; set; } = null;

        /// <summary>
        /// Thread context, or null.
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// Forum context, or null.
        /// </summary>
        public string? ForumId { get; set; } = null;

        /// <summary>
        /// Poll context, or null.
        /// </summary>
        public string? PollId { get; set; } = null;

        /// <summary>
        /// Aggregation group key (ThreadReply and NewThreadInForum), or null.
        /// </summary>
        public string? GroupKey { get; set; } = null;

        /// <summary>
        /// Noun for the subject in the title ("post", "comment", "member"), or null to derive it from the target type
        /// (FeedPost post, Comment comment, User member, anything else item).
        /// </summary>
        public string? SubjectNoun { get; set; } = null;

        /// <summary>
        /// Extra title text: the post title for NewFeedPost ("Example Owner posted: &lt;title&gt;"), or null.
        /// </summary>
        public string? TitleText { get; set; } = null;

        /// <summary>
        /// ModerationAction only: what the moderator did (Hide, Unhide, Remove, Restore). Default Remove.
        /// </summary>
        public ModerationActionTypeEnum? ModerationAction { get; set; } = null;

        /// <summary>
        /// ModerationAction only: reason shown in parentheses, for example "Spam", or null.
        /// </summary>
        public string? ModerationReason { get; set; } = null;

        /// <summary>
        /// MembershipExpiring only: when the membership ends (rendered "March 1, 2027").
        /// </summary>
        public DateTime? EventUtc { get; set; } = null;

        /// <summary>
        /// Excerpt of the related content, or null.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// App-relative path, or null to derive it: /feed/{feedPostId}?comment={commentId} when both are set,
        /// /feed/{feedPostId} with only a post, /account/membership for MembershipExpiring, /notifications otherwise.
        /// </summary>
        public string? Path { get; set; } = null;

        /// <summary>
        /// Queue a push for it (pushstate Pending). Default true; the push worker then applies preferences, pause,
        /// notification hours, tokens, and rate limits.
        /// </summary>
        public bool QueuePush { get; set; } = true;

        /// <summary>
        /// A shallow copy for another recipient.
        /// </summary>
        /// <param name="recipientUserId">Recipient.</param>
        /// <returns>Copy.</returns>
        public NotificationDraft For(string recipientUserId)
        {
            NotificationDraft copy = (NotificationDraft)MemberwiseClone();
            copy.RecipientUserId = recipientUserId;
            return copy;
        }
    }
}
