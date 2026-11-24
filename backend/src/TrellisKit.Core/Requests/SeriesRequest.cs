namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/admin/series and PUT /api/v1/admin/series/{id} (omitted fields stay on PUT).
    /// </summary>
    public class SeriesRequest
    {
        #region Public-Members

        /// <summary>
        /// Slug (2 to 80 of a-z, 0-9, -); derived from the title when omitted on create.
        /// </summary>
        public string? Slug { get; set; } = null;

        /// <summary>
        /// Title (2 to 120 characters).
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Summary (up to 300 characters).
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Description (Markdown subset, up to 10000 characters).
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Ready image media, or null.
        /// </summary>
        public string? CoverMediaId { get; set; } = null;

        /// <summary>
        /// Level keys (at most 10).
        /// </summary>
        public List<string>? Levels { get; set; } = null;

        /// <summary>
        /// Audience (Public only for SystemAdmin).
        /// </summary>
        public ContentAudienceEnum? Audience { get; set; } = null;

        /// <summary>
        /// When narrowing the audience, narrow lessons that had the old audience (default true).
        /// </summary>
        public bool? NarrowLessons { get; set; } = null;

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
