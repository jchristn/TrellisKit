namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The fields of a directory instructor an event shows.
    /// </summary>
    public class EventInstructorSnapshot
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ins_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Slug.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Display name.
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>
        /// Photo media, or null.
        /// </summary>
        public string? PhotoMediaId { get; set; } = null;

        /// <summary>
        /// Audience.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Whether the instructor is active, not deleted, and not moderated.
        /// </summary>
        public bool IsListed { get; set; } = false;

        #endregion
    }
}
