namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The Idea payload (docs/PHASE5_CONTRACT.md section 2.4). In lists (IdeaSummary) body is null and excerpt is set.
    /// </summary>
    public class IdeaView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ida_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Body (Markdown subset); null in lists; empty on a locked stub.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Plain-text excerpt; empty on a locked stub.
        /// </summary>
        public string Excerpt { get; set; } = String.Empty;

        /// <summary>
        /// Author, or null when hidden (staff-published ideas may hide the requester).
        /// </summary>
        public UserSummary? Author { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public IdeaStatusRefView Status
        {
            get { return _Status; }
            set { _Status = value ?? new IdeaStatusRefView(); }
        }

        /// <summary>
        /// Status note, or null.
        /// </summary>
        public string? StatusNote { get; set; } = null;

        /// <summary>
        /// When the status last changed, or null.
        /// </summary>
        public DateTime? StatusUtc { get; set; } = null;

        /// <summary>
        /// Public vote count.
        /// </summary>
        public int VoteCount { get; set; } = 0;

        /// <summary>
        /// Whether the caller voted.
        /// </summary>
        public bool HasVoted { get; set; } = false;

        /// <summary>
        /// Whether the caller follows the idea.
        /// </summary>
        public bool IsFollowed { get; set; } = false;

        /// <summary>
        /// The idea this one was merged into, or null.
        /// </summary>
        public IdeaMergedRefView? MergedInto { get; set; } = null;

        /// <summary>
        /// The conversation target of comments and reactions ({ type: Idea, id }).
        /// </summary>
        public ContentTarget CommentTarget
        {
            get { return _CommentTarget; }
            set { _CommentTarget = value ?? new ContentTarget(); }
        }

        /// <summary>
        /// Visible comments.
        /// </summary>
        public int CommentCount { get; set; } = 0;

        /// <summary>
        /// Reaction counts by key.
        /// </summary>
        public Dictionary<string, long> ReactionCounts
        {
            get { return _ReactionCounts; }
            set { _ReactionCounts = value ?? new Dictionary<string, long>(); }
        }

        /// <summary>
        /// Reaction total.
        /// </summary>
        public long ReactionTotal { get; set; } = 0;

        /// <summary>
        /// The caller's reactions.
        /// </summary>
        public List<string> MyReactions
        {
            get { return _MyReactions; }
            set { _MyReactions = value ?? new List<string>(); }
        }

        /// <summary>
        /// Audience.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Whether this is a locked stub (PartiallyGated, signed-in non-member, Members idea).
        /// </summary>
        public bool Locked { get; set; } = false;

        /// <summary>
        /// Moderation state for the author and staff, or null.
        /// </summary>
        public ModerationInfo? Moderation { get; set; } = null;

        /// <summary>
        /// Staff only: the ticket the idea was published from, or null.
        /// </summary>
        public string? SourceTicketId { get; set; } = null;

        /// <summary>
        /// Staff only: votes by membership state, or null.
        /// </summary>
        public IdeaVoterCounts? VoterCounts { get; set; } = null;

        /// <summary>
        /// Last edit, or null.
        /// </summary>
        public DateTime? EditedUtc { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        /// <summary>
        /// What the caller may do.
        /// </summary>
        public IdeaViewerView Viewer
        {
            get { return _Viewer; }
            set { _Viewer = value ?? new IdeaViewerView(); }
        }

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private IdeaStatusRefView _Status = new IdeaStatusRefView();
        private ContentTarget _CommentTarget = new ContentTarget();
        private Dictionary<string, long> _ReactionCounts = new Dictionary<string, long>();
        private List<string> _MyReactions = new List<string>();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();
        private IdeaViewerView _Viewer = new IdeaViewerView();

        #endregion
    }
}
