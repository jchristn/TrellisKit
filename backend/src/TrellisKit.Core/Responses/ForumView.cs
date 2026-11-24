namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A forum (docs/PHASE3_CONTRACT.md section 2.1).
    /// </summary>
    public class ForumView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (frm_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Slug.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Guidelines (Markdown subset), or null.
        /// </summary>
        public string? Guidelines { get; set; } = null;

        /// <summary>
        /// Icon.
        /// </summary>
        public ForumIconView Icon
        {
            get { return _Icon; }
            set { _Icon = value ?? new ForumIconView(); }
        }

        /// <summary>
        /// Color as #RRGGBB, or null.
        /// </summary>
        public string? Color { get; set; } = null;

        /// <summary>
        /// Section header, or null.
        /// </summary>
        public string? Section { get; set; } = null;

        /// <summary>
        /// Position in the forum list.
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
        /// Allowed thread types.
        /// </summary>
        public List<ThreadTypeEnum> AllowedThreadTypes
        {
            get { return _AllowedThreadTypes; }
            set { _AllowedThreadTypes = value ?? new List<ThreadTypeEnum>(); }
        }

        /// <summary>
        /// Default thread ordering.
        /// </summary>
        public ThreadOrderingEnum DefaultOrdering { get; set; } = ThreadOrderingEnum.Active;

        /// <summary>
        /// Whether media is allowed.
        /// </summary>
        public bool AllowMedia { get; set; } = true;

        /// <summary>
        /// Whether video is allowed.
        /// </summary>
        public bool AllowVideo { get; set; } = true;

        /// <summary>
        /// Open Feedback threads per member in this forum (null uses the property setting; 0 is unlimited).
        /// </summary>
        public int? MaxOpenFeedbackPerMember { get; set; } = null;

        /// <summary>
        /// Whether the forum is read-only.
        /// </summary>
        public bool IsArchived { get; set; } = false;

        /// <summary>
        /// Published threads.
        /// </summary>
        public int ThreadCount { get; set; } = 0;

        /// <summary>
        /// Visible comments on published threads.
        /// </summary>
        public int CommentCount { get; set; } = 0;

        /// <summary>
        /// Last activity, or null.
        /// </summary>
        public DateTime? LastActivityUtc { get; set; } = null;

        /// <summary>
        /// Most recently active thread, or null.
        /// </summary>
        public ForumLastThreadView? LastThread { get; set; } = null;

        /// <summary>
        /// Whether the caller follows the forum.
        /// </summary>
        public bool IsFollowed { get; set; } = false;

        /// <summary>
        /// Threads with activity since the caller last read the forum (capped at 99), or null for anonymous callers.
        /// </summary>
        public int? UnreadThreadCount { get; set; } = null;

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
        public ForumViewer Viewer
        {
            get { return _Viewer; }
            set { _Viewer = value ?? new ForumViewer(); }
        }

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private ForumIconView _Icon = new ForumIconView();
        private List<ThreadTypeEnum> _AllowedThreadTypes = new List<ThreadTypeEnum>();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();
        private ForumViewer _Viewer = new ForumViewer();

        #endregion
    }
}
