namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A row of the notifications table: one entry in a member's notification center.
    /// </summary>
    public class Notification
    {
        /// <summary>
        /// Identifier (ntf_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Recipient member.
        /// </summary>
        public string RecipientUserId { get; set; } = String.Empty;

        /// <summary>
        /// Type.
        /// </summary>
        public NotificationTypeEnum Type { get; set; } = NotificationTypeEnum.PostComment;

        /// <summary>
        /// Category (derived from the type).
        /// </summary>
        public NotificationCategoryEnum Category { get; set; } = NotificationCategoryEnum.Replies;

        /// <summary>
        /// Most recent actor, or null for system notifications.
        /// </summary>
        public string? ActorUserId { get; set; } = null;

        /// <summary>
        /// Distinct actors (1 for non-aggregated notifications with an actor, 0 for system notifications).
        /// </summary>
        public int ActorCount { get; set; } = 0;

        /// <summary>
        /// Target type as stored (for example FeedPost or Comment). Unknown values are kept as text.
        /// </summary>
        public string TargetType { get; set; } = String.Empty;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Related feed post, or null.
        /// </summary>
        public string? FeedPostId { get; set; } = null;

        /// <summary>
        /// Related comment, or null.
        /// </summary>
        public string? CommentId { get; set; } = null;

        /// <summary>
        /// Parent of the related comment, or null.
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
        /// Server-rendered English title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Excerpt of the related content, or null.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// App-relative path, for example /feed/fpo_...
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Aggregation group key (Reaction:&lt;targetType&gt;:&lt;targetId&gt;), or null.
        /// </summary>
        public string? GroupKey { get; set; } = null;

        /// <summary>
        /// When the member read it, or null while unread.
        /// </summary>
        public DateTime? ReadUtc { get; set; } = null;

        /// <summary>
        /// Push delivery state.
        /// </summary>
        public PushStateEnum PushState { get; set; } = PushStateEnum.None;

        /// <summary>
        /// Last time a push was sent for it, or null.
        /// </summary>
        public DateTime? LastPushedUtc { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update (UTC); aggregation moves it forward.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Up to three most recent actors, newest first (filled by list reads; empty otherwise). Never null.
        /// </summary>
        public List<string> RecentActorIds
        {
            get { return _RecentActorIds; }
            set { _RecentActorIds = value ?? new List<string>(); }
        }

        private List<string> _RecentActorIds = new List<string>();
    }
}
