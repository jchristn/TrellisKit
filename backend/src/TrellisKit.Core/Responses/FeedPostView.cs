namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// FeedPost payload (contract section 5.1) plus audience, labels, and tags. Nested lists are fixed-bound: attachments (feed.maxAttachments), mentions (mentions.maxNotifiedPerItem), labels and tags (50 each).
    /// </summary>
    public class FeedPostView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Kind.
        /// </summary>
        public FeedPostKindEnum Kind { get; set; } = FeedPostKindEnum.Original;

        /// <summary>
        /// Author.
        /// </summary>
        public UserSummary Author { get; set; } = new UserSummary();

        /// <summary>
        /// Title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Markdown body.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Plain-text excerpt.
        /// </summary>
        public string Excerpt { get; set; } = String.Empty;

        /// <summary>
        /// Mentions (never null).
        /// </summary>
        public List<MentionView> Mentions
        {
            get { return _Mentions; }
            set { _Mentions = value ?? new List<MentionView>(); }
        }

        /// <summary>
        /// Attachments in display order (never null).
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
        /// Members or Public.
        /// </summary>
        public FeedVisibilityEnum Visibility { get; set; } = FeedVisibilityEnum.Members;

        /// <summary>
        /// Audience.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Whether this is a locked stub (docs/ACCESS_MODELS_CONTRACT.md section 3.5): PartiallyGated, a signed-in caller
        /// without a membership, a Members post. A stub keeps only discovery fields (id, kind, author, title, audience,
        /// dates, pin state, commentCount, labels, tags); body and excerpt are empty, attachments, mentions, and
        /// reactions are empty, linkPreview is null, and viewer.canOpen is false with viewer.requires Membership.
        /// Default false.
        /// </summary>
        public bool Locked { get; set; } = false;

        /// <summary>
        /// State.
        /// </summary>
        public FeedPostStateEnum State { get; set; } = FeedPostStateEnum.Published;

        /// <summary>
        /// Whether comments are allowed.
        /// </summary>
        public bool CommentsEnabled { get; set; } = true;

        /// <summary>
        /// Whether the author wants a preview card.
        /// </summary>
        public bool ShowLinkPreview { get; set; } = true;

        /// <summary>
        /// Pinned.
        /// </summary>
        public bool IsPinned { get; set; } = false;

        /// <summary>
        /// When pinned.
        /// </summary>
        public DateTime? PinnedUtc { get; set; } = null;

        /// <summary>
        /// Scheduled time.
        /// </summary>
        public DateTime? PublishAtUtc { get; set; } = null;

        /// <summary>
        /// When it first became visible.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last edit after the grace period.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        /// <summary>
        /// Stored revisions.
        /// </summary>
        public int RevisionCount { get; set; } = 0;

        /// <summary>
        /// Visible comments.
        /// </summary>
        public int CommentCount { get; set; } = 0;

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
        /// Where comments and reactions live.
        /// </summary>
        public ContentTarget CommentTarget { get; set; } = new ContentTarget();

        /// <summary>
        /// Promoted source (Phase 3), or null.
        /// </summary>
        public FeedPostSourceView? Source { get; set; } = null;

        /// <summary>
        /// The discussion thread opened from this post in a forum, or null.
        /// </summary>
        public FeedPostDiscussionView? Discussion { get; set; } = null;

        /// <summary>
        /// For the author and moderators when Hidden or Removed (always for the moderator view), else null.
        /// </summary>
        public ModerationInfo? Moderation { get; set; } = null;

        /// <summary>
        /// Caller capabilities.
        /// </summary>
        public FeedPostViewer Viewer { get; set; } = new FeedPostViewer();

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
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
