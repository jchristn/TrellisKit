namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The directory configuration stored for the property.
    /// </summary>
    public class DirectoryConfigView
    {
        #region Public-Members

        /// <summary>
        /// Venue label.
        /// </summary>
        public string VenueLabel { get; set; } = "Venue";

        /// <summary>
        /// Venue label plural.
        /// </summary>
        public string VenueLabelPlural { get; set; } = "Venues";

        /// <summary>
        /// Instructor label.
        /// </summary>
        public string InstructorLabel { get; set; } = "Instructor";

        /// <summary>
        /// Instructor label plural.
        /// </summary>
        public string InstructorLabelPlural { get; set; } = "Instructors";

        /// <summary>
        /// Session label.
        /// </summary>
        public string SessionLabel { get; set; } = "Session";

        /// <summary>
        /// Session label plural.
        /// </summary>
        public string SessionLabelPlural { get; set; } = "Sessions";

        /// <summary>
        /// Rank profile field key.
        /// </summary>
        public string? InstructorRankFieldKey { get; set; } = null;

        /// <summary>
        /// Member submissions on.
        /// </summary>
        public bool Submissions { get; set; } = true;

        /// <summary>
        /// Instructor claims on.
        /// </summary>
        public bool Claims { get; set; } = true;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
