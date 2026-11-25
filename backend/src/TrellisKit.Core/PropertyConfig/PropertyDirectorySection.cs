namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The optional property.json directory section (docs/PHASE4_CONTRACT.md section 0.2). Absent means the engine defaults shown; seeded into the database on first load and editable in the admin console afterwards.
    /// </summary>
    public class PropertyDirectorySection
    {
        #region Public-Members

        /// <summary>Optional directory introduction supplied by the property.</summary>
        public string? Introduction { get; set; } = null;

        /// <summary>Optional listing notice supplied by the property.</summary>
        public string? ListingNotice { get; set; } = null;

        /// <summary>
        /// What the property calls one directory entry (1 to 40 characters). Default "Venue".
        /// </summary>
        public string VenueLabel { get; set; } = "Venue";

        /// <summary>
        /// Plural of the venue label (1 to 40 characters). Default "Venues".
        /// </summary>
        public string VenueLabelPlural { get; set; } = "Venues";

        /// <summary>
        /// What the property calls one listed person (1 to 40 characters). Default "Instructor".
        /// </summary>
        public string InstructorLabel { get; set; } = "Instructor";

        /// <summary>
        /// Plural of the instructor label (1 to 40 characters). Default "Instructors".
        /// </summary>
        public string InstructorLabelPlural { get; set; } = "Instructors";

        /// <summary>
        /// Program options a venue may offer (0 to 50; keys stable, labels free). Default empty.
        /// </summary>
        public List<PropertyOptionSeed> Programs { get; set; } = new List<PropertyOptionSeed>();

        /// <summary>
        /// Key of a Rank profile field whose options rank instructors, or null to hide rank on instructors. Default null.
        /// </summary>
        public string? InstructorRankFieldKey { get; set; } = null;

        /// <summary>
        /// Whether members may submit new entries, edits, and closures through the review queue. Default true.
        /// </summary>
        public bool Submissions { get; set; } = true;

        /// <summary>
        /// Whether members may claim an instructor entry as themselves. Default true.
        /// </summary>
        public bool Claims { get; set; } = true;

        #endregion
    }
}
