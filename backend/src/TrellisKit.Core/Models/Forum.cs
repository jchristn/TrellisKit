namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A forum row (docs/PHASE3_CONTRACT.md sections 2.1 and 17).
    /// </summary>
    public class Forum
    {
        #region Public-Members

        /// <summary>
        /// Identifier (frm_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// URL slug, unique per property case-insensitively.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Display name (2 to 60 characters).
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description (up to 500 characters), or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Guidelines in the post Markdown subset (up to 5000 characters), or null.
        /// </summary>
        public string? Guidelines { get; set; } = null;

        /// <summary>
        /// Icon emoji (one grapheme), or null.
        /// </summary>
        public string? IconEmoji { get; set; } = null;

        /// <summary>
        /// Icon image media identifier, or null.
        /// </summary>
        public string? IconMediaId { get; set; } = null;

        /// <summary>
        /// Color as #RRGGBB, or null.
        /// </summary>
        public string? Color { get; set; } = null;

        /// <summary>
        /// Grouping header (0 to 60 characters), or null.
        /// </summary>
        public string? Section { get; set; } = null;

        /// <summary>
        /// Position in the forum list (ascending).
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// Who may read the forum.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Who may create threads.
        /// </summary>
        public ThreadCreationEnum ThreadCreation { get; set; } = ThreadCreationEnum.Members;

        /// <summary>
        /// Allowed thread types (non-empty subset of the four types).
        /// </summary>
        public List<ThreadTypeEnum> AllowedThreadTypes
        {
            get { return _AllowedThreadTypes; }
            set { _AllowedThreadTypes = value ?? new List<ThreadTypeEnum>(); }
        }

        /// <summary>
        /// Ordering clients use when the member has not chosen one.
        /// </summary>
        public ThreadOrderingEnum DefaultOrdering { get; set; } = ThreadOrderingEnum.Active;

        /// <summary>
        /// Whether threads and comments may carry media.
        /// </summary>
        public bool AllowMedia { get; set; } = true;

        /// <summary>
        /// Whether threads and comments may carry video.
        /// </summary>
        public bool AllowVideo { get; set; } = true;

        /// <summary>
        /// Open Feedback threads per member in this forum (null uses forums.maxOpenFeedbackPerMember; 0 is unlimited).
        /// </summary>
        public int? MaxOpenFeedbackPerMember { get; set; } = null;

        /// <summary>
        /// Whether the forum is read-only.
        /// </summary>
        public bool IsArchived { get; set; } = false;

        /// <summary>
        /// When the forum was archived, or null.
        /// </summary>
        public DateTime? ArchivedUtc { get; set; } = null;

        /// <summary>
        /// Published, unmoderated, undeleted threads.
        /// </summary>
        public int ThreadCount { get; set; } = 0;

        /// <summary>
        /// Visible comments on those threads.
        /// </summary>
        public int CommentCount { get; set; } = 0;

        /// <summary>
        /// Last activity of any thread, or null.
        /// </summary>
        public DateTime? LastActivityUtc { get; set; } = null;

        /// <summary>
        /// Thread with the latest activity, or null.
        /// </summary>
        public string? LastThreadId { get; set; } = null;

        /// <summary>
        /// Creator, or null for seeded forums.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// Deletion time, or null.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Creation time (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<ThreadTypeEnum> _AllowedThreadTypes = new List<ThreadTypeEnum>();

        #endregion
    }
}
