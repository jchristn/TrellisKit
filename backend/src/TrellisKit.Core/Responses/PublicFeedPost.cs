namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Anonymous public feed payload (contract section 5.10): the FeedPost without myReactions, viewer, moderation, and mentions (handles render as plain text); media served from the public paths.
    /// </summary>
    public class PublicFeedPost
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
        /// Attachments (public paths) (never null).
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
        /// Always Public.
        /// </summary>
        public FeedVisibilityEnum Visibility { get; set; } = FeedVisibilityEnum.Public;

        /// <summary>
        /// Always Public.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Public;

        /// <summary>
        /// Always Published.
        /// </summary>
        public FeedPostStateEnum State { get; set; } = FeedPostStateEnum.Published;

        /// <summary>
        /// Whether comments are allowed (members only).
        /// </summary>
        public bool CommentsEnabled { get; set; } = true;

        /// <summary>
        /// Pinned.
        /// </summary>
        public bool IsPinned { get; set; } = false;

        /// <summary>
        /// Published.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Edited.
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
        /// Where comments and reactions live.
        /// </summary>
        public ContentTarget CommentTarget { get; set; } = new ContentTarget();

        /// <summary>
        /// Public labels (never null).
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Public tags (never null).
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private List<MediaView> _Attachments = new List<MediaView>();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
