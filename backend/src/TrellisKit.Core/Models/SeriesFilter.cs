namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Filter of the series enumerations (member and admin).
    /// </summary>
    public class SeriesFilter
    {
        #region Public-Members

        /// <summary>
        /// Level keys (any).
        /// </summary>
        public List<string> Levels { get; set; } = new List<string>();

        /// <summary>
        /// Series followed by this member, or null.
        /// </summary>
        public string? FollowedByUserId { get; set; } = null;

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
        /// Series the caller cannot read are still listed when they hold a published lesson of these audiences (locked stubs that list open lessons), or null.
        /// </summary>
        public List<ContentAudienceEnum>? LessonAudiences { get; set; } = null;

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
        /// Restrict to these series (search), or null.
        /// </summary>
        public List<string>? Ids { get; set; } = null;

        #endregion
    }
}
