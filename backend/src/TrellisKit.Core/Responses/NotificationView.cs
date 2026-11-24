namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The Notification payload (Phase 2 contract 8.1).
    /// </summary>
    public class NotificationView
    {
        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Type.
        /// </summary>
        public NotificationTypeEnum Type { get; set; } = NotificationTypeEnum.PostComment;

        /// <summary>
        /// Category.
        /// </summary>
        public NotificationCategoryEnum Category { get; set; } = NotificationCategoryEnum.Replies;

        /// <summary>
        /// Server-rendered English title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Excerpt of the related content, or null.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Most recent actor, or null for system notifications.
        /// </summary>
        public UserSummary? Actor { get; set; } = null;

        /// <summary>
        /// Up to three most recent actors, newest first (bounded). Never null.
        /// </summary>
        public List<UserSummary> Actors
        {
            get { return _Actors; }
            set { _Actors = value ?? new List<UserSummary>(); }
        }

        /// <summary>
        /// Total distinct actors.
        /// </summary>
        public int ActorCount { get; set; } = 0;

        /// <summary>
        /// Target.
        /// </summary>
        public NotificationTarget Target { get; set; } = new NotificationTarget();

        /// <summary>
        /// Deep link ids (feedPostId, commentId, parentCommentId); keys not relevant to the type are omitted. Never null.
        /// </summary>
        public Dictionary<string, string> Context
        {
            get { return _Context; }
            set { _Context = value ?? new Dictionary<string, string>(); }
        }

        /// <summary>
        /// App-relative route.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Whether it was read.
        /// </summary>
        public bool IsRead { get; set; } = false;

        /// <summary>
        /// When it was read, or null.
        /// </summary>
        public DateTime? ReadUtc { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels (enumeration and metadata specification). Never null.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags (enumeration and metadata specification). Never null.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        private List<UserSummary> _Actors = new List<UserSummary>();
        private Dictionary<string, string> _Context = new Dictionary<string, string>();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();
    }
}
