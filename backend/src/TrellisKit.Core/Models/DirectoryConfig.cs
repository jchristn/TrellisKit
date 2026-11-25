namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// The directory configuration of a property (docs/PHASE4_CONTRACT.md sections 0.2 and 14).
    /// </summary>
    public class DirectoryConfig
    {
        #region Public-Members

        /// <summary>
        /// What a venue is called.
        /// </summary>
        public string VenueLabel { get; set; } = "Venue";

        /// <summary>
        /// Plural of the venue label.
        /// </summary>
        public string VenueLabelPlural { get; set; } = "Venues";

        /// <summary>
        /// What an instructor is called.
        /// </summary>
        public string InstructorLabel { get; set; } = "Instructor";

        /// <summary>
        /// Plural of the instructor label.
        /// </summary>
        public string InstructorLabelPlural { get; set; } = "Instructors";

        /// <summary>
        /// What a weekly scheduled session is called.
        /// </summary>
        public string SessionLabel { get; set; } = "Session";

        /// <summary>
        /// Plural of the session label.
        /// </summary>
        public string SessionLabelPlural { get; set; } = "Sessions";

        /// <summary>
        /// Key of the Rank profile field instructors carry, or null.
        /// </summary>
        public string? InstructorRankFieldKey { get; set; } = null;

        /// <summary>
        /// Member submission queue on or off.
        /// </summary>
        public bool SubmissionsEnabled { get; set; } = true;

        /// <summary>
        /// Instructor claims on or off.
        /// </summary>
        public bool ClaimsEnabled { get; set; } = true;

        /// <summary>
        /// Hash of the property.json section last seeded, or null.
        /// </summary>
        public string? SourceHash { get; set; } = null;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
