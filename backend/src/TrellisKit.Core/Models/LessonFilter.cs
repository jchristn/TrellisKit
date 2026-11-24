namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Filter of the lesson enumerations (member and admin).
    /// </summary>
    public class LessonFilter
    {
        #region Public-Members

        /// <summary>
        /// Series (empty for all).
        /// </summary>
        public List<string> SeriesIds { get; set; } = new List<string>();

        /// <summary>
        /// Level keys (any).
        /// </summary>
        public List<string> Levels { get; set; } = new List<string>();

        /// <summary>
        /// Kind, or null.
        /// </summary>
        public LessonKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Lessons with (true) or without (false) resources, or null.
        /// </summary>
        public bool? HasResources { get; set; } = null;

        /// <summary>
        /// Shortest duration, or null.
        /// </summary>
        public int? MinDurationSeconds { get; set; } = null;

        /// <summary>
        /// Longest duration, or null.
        /// </summary>
        public int? MaxDurationSeconds { get; set; } = null;

        /// <summary>
        /// Progress of ProgressUserId, or null.
        /// </summary>
        public LessonProgressStateEnum? Progress { get; set; } = null;

        /// <summary>
        /// Member for the progress filter.
        /// </summary>
        public string? ProgressUserId { get; set; } = null;

        /// <summary>
        /// Audiences listed in full, or null for all.
        /// </summary>
        public List<ContentAudienceEnum>? Audiences { get; set; } = null;

        /// <summary>
        /// Additional audiences listed as locked stubs, or null.
        /// </summary>
        public List<ContentAudienceEnum>? StubAudiences { get; set; } = null;

        /// <summary>
        /// Audience filter, or null.
        /// </summary>
        public ContentAudienceEnum? Audience { get; set; } = null;

        /// <summary>
        /// Admin list (every status, deleted excluded).
        /// </summary>
        public bool Admin { get; set; } = false;

        /// <summary>
        /// Statuses (admin; empty for every status).
        /// </summary>
        public List<LibraryStatusEnum> Statuses { get; set; } = new List<LibraryStatusEnum>();

        /// <summary>
        /// Lessons with (true) or without (false) a video (admin), or null.
        /// </summary>
        public bool? HasVideo { get; set; } = null;

        /// <summary>
        /// Video media status (admin), or null.
        /// </summary>
        public MediaStatusEnum? MediaStatus { get; set; } = null;

        /// <summary>
        /// Restrict to these lessons (search), or null.
        /// </summary>
        public List<string>? Ids { get; set; } = null;

        /// <summary>
        /// Public sample lessons only.
        /// </summary>
        public bool PublicOnly { get; set; } = false;

        #endregion
    }
}
