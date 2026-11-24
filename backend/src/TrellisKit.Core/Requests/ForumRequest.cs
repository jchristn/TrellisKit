namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Create or update a forum (POST and PUT /api/v1/admin/forums).
    /// </summary>
    public class ForumRequest
    {
        #region Public-Members

        /// <summary>
        /// Name (2 to 60 characters).
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Slug (2 to 60 of a-z, 0-9, and -); derived from the name when omitted on create.
        /// </summary>
        public string? Slug { get; set; } = null;

        /// <summary>
        /// Description (0 to 500 characters).
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Guidelines (0 to 5000 characters, Markdown subset).
        /// </summary>
        public string? Guidelines { get; set; } = null;

        /// <summary>
        /// Icon: an emoji or an image media identifier.
        /// </summary>
        public ForumIconRequest? Icon { get; set; } = null;

        /// <summary>
        /// Color as #RRGGBB, or null.
        /// </summary>
        public string? Color { get; set; } = null;

        /// <summary>
        /// Section header (0 to 60 characters).
        /// </summary>
        public string? Section { get; set; } = null;

        /// <summary>
        /// Audience (default access.defaultAudience, Members).
        /// </summary>
        public ContentAudienceEnum? Audience { get; set; } = null;

        /// <summary>
        /// Who may create threads (default Members).
        /// </summary>
        public ThreadCreationEnum? ThreadCreation { get; set; } = null;

        /// <summary>
        /// Allowed thread types (default all four).
        /// </summary>
        public List<ThreadTypeEnum>? AllowedThreadTypes { get; set; } = null;

        /// <summary>
        /// Default ordering (default Active).
        /// </summary>
        public ThreadOrderingEnum? DefaultOrdering { get; set; } = null;

        /// <summary>
        /// Whether media is allowed (default true).
        /// </summary>
        public bool? AllowMedia { get; set; } = null;

        /// <summary>
        /// Whether video is allowed (default true).
        /// </summary>
        public bool? AllowVideo { get; set; } = null;

        /// <summary>
        /// Open Feedback threads per member (null uses the property setting; 0 is unlimited).
        /// </summary>
        public int? MaxOpenFeedbackPerMember { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
