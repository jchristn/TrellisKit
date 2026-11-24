namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Create or edit an idea (POST /api/v1/ideas, PUT /api/v1/ideas/{id}, POST and PUT /api/v1/admin/ideas). Omitted
    /// fields stay unchanged on edits.
    /// </summary>
    public class IdeaRequest
    {
        #region Public-Members

        /// <summary>
        /// Title (3 to 140 characters).
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Body (Markdown subset, 0 to 10000 characters).
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Audience (Public needs SystemAdmin or SupportAgent), or null for the default.
        /// </summary>
        public ContentAudienceEnum? Audience { get; set; } = null;

        /// <summary>
        /// Staff only: initial status key, or null for the initial status.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Staff only: whether the author is shown (default true).
        /// </summary>
        public bool? ShowAuthor { get; set; } = null;

        /// <summary>
        /// Labels (replace semantics), or null.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags (replace semantics), or null.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
