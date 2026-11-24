namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Comment payload (contract section 7.2) plus labels, tags, and moderation. Placeholders (Deleted, Removed, Hidden, BlockedAuthor) carry no author, body, mentions, attachments, preview, reactions, labels, or tags, and every viewer flag is false. children is bounded by the tree page limits (childLimit per level, depth levels).
    /// </summary>
    public class CommentView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Target type.
        /// </summary>
        public string TargetType { get; set; } = String.Empty;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Parent, or null.
        /// </summary>
        public string? ParentCommentId { get; set; } = null;

        /// <summary>
        /// Replied-to comment when flattened at the maximum depth, or null.
        /// </summary>
        public string? ReplyToCommentId { get; set; } = null;

        /// <summary>
        /// Author of replyToCommentId, or null.
        /// </summary>
        public UserSummary? ReplyToUser { get; set; } = null;

        /// <summary>
        /// Depth.
        /// </summary>
        public int Depth { get; set; } = 0;

        /// <summary>
        /// State for this viewer.
        /// </summary>
        public CommentStateEnum State { get; set; } = CommentStateEnum.Visible;

        /// <summary>
        /// Author (null on placeholders).
        /// </summary>
        public UserSummary? Author { get; set; } = null;

        /// <summary>
        /// Body (null on placeholders).
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Mentions (never null).
        /// </summary>
        public List<MentionView> Mentions
        {
            get { return _Mentions; }
            set { _Mentions = value ?? new List<MentionView>(); }
        }

        /// <summary>
        /// Attachments (never null).
        /// </summary>
        public List<MediaView> Attachments
        {
            get { return _Attachments; }
            set { _Attachments = value ?? new List<MediaView>(); }
        }

        /// <summary>
        /// Preview card, or null.
        /// </summary>
        public LinkPreviewView? LinkPreview { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last edit after the grace period.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        /// <summary>
        /// Stored revisions (0 on placeholders).
        /// </summary>
        public int RevisionCount { get; set; } = 0;

        /// <summary>
        /// Visible direct replies.
        /// </summary>
        public int ChildCount { get; set; } = 0;

        /// <summary>
        /// Visible replies at any depth.
        /// </summary>
        public int DescendantCount { get; set; } = 0;

        /// <summary>
        /// Whether this is the accepted answer of its Question thread.
        /// </summary>
        public bool IsAcceptedAnswer { get; set; } = false;

        /// <summary>
        /// Whether this is the chosen review of its Feedback thread.
        /// </summary>
        public bool IsFeedbackReview { get; set; } = false;

        /// <summary>
        /// Counts of current keys above zero.
        /// </summary>
        public Dictionary<string, long> ReactionCounts { get; set; } = new Dictionary<string, long>();

        /// <summary>
        /// Sum of reactionCounts.
        /// </summary>
        public long ReactionTotal { get; set; } = 0;

        /// <summary>
        /// The caller's keys (never null).
        /// </summary>
        public List<string> MyReactions
        {
            get { return _MyReactions; }
            set { _MyReactions = value ?? new List<string>(); }
        }

        /// <summary>
        /// For the author and moderators when Hidden or Removed (always for the moderator view), else null.
        /// </summary>
        public ModerationInfo? Moderation { get; set; } = null;

        /// <summary>
        /// Caller capabilities.
        /// </summary>
        public CommentViewer Viewer { get; set; } = new CommentViewer();

        /// <summary>
        /// Nested replies (bounded by depth and childLimit) (never null).
        /// </summary>
        public List<CommentView> Children
        {
            get { return _Children; }
            set { _Children = value ?? new List<CommentView>(); }
        }

        /// <summary>
        /// Replies not included, or null.
        /// </summary>
        public MoreChildrenView? MoreChildren { get; set; } = null;

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

        private List<MentionView> _Mentions = new List<MentionView>();
        private List<MediaView> _Attachments = new List<MediaView>();
        private List<string> _MyReactions = new List<string>();
        private List<CommentView> _Children = new List<CommentView>();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
