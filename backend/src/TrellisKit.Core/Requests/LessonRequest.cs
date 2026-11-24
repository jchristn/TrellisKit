namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/admin/series/{id}/lessons and PUT /api/v1/admin/lessons/{id} (omitted fields stay on PUT; lists replace).
    /// </summary>
    public class LessonRequest
    {
        #region Public-Members

        /// <summary>
        /// Slug (2 to 80 of a-z, 0-9, -), unique per series.
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
        /// Description (Markdown subset; up to library.maxArticleLength).
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Kind (default Video).
        /// </summary>
        public LessonKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Video media (Video lessons).
        /// </summary>
        public string? VideoMediaId { get; set; } = null;

        /// <summary>
        /// Level keys (at most 10).
        /// </summary>
        public List<string>? Levels { get; set; } = null;

        /// <summary>
        /// Audience (default the series audience; Public only for SystemAdmin).
        /// </summary>
        public ContentAudienceEnum? Audience { get; set; } = null;

        /// <summary>
        /// Staff download switch (default true).
        /// </summary>
        public bool? Downloadable { get; set; } = null;

        /// <summary>
        /// Comments on (default library.commentsEnabledByDefault).
        /// </summary>
        public bool? CommentsEnabled { get; set; } = null;

        /// <summary>
        /// Notify library staff of new comments (default false).
        /// </summary>
        public bool? NotifyStaffOnComment { get; set; } = null;

        /// <summary>
        /// Markers (replaces the list).
        /// </summary>
        public List<LessonMarkerInput>? Markers { get; set; } = null;

        /// <summary>
        /// Resources (replaces the list).
        /// </summary>
        public List<LessonResourceInput>? Resources { get; set; } = null;

        /// <summary>
        /// Captions (replaces the list).
        /// </summary>
        public List<LessonCaptionInput>? Captions { get; set; } = null;

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
