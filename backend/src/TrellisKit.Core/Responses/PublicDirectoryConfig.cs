namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The effective directory configuration clients render from (docs/PHASE4_CONTRACT.md section 0.2).
    /// </summary>
    public class PublicDirectoryConfig
    {
        #region Public-Members

        /// <summary>Optional directory introduction supplied by the property.</summary>
        public string? Introduction { get; set; } = null;

        /// <summary>Optional listing notice supplied by the property.</summary>
        public string? ListingNotice { get; set; } = null;

        /// <summary>
        /// Venue label (the {venue} placeholder).
        /// </summary>
        public string VenueLabel { get; set; } = "Venue";

        /// <summary>
        /// Plural venue label (the {venues} placeholder).
        /// </summary>
        public string VenueLabelPlural { get; set; } = "Venues";

        /// <summary>
        /// Instructor label (the {instructor} placeholder).
        /// </summary>
        public string InstructorLabel { get; set; } = "Instructor";

        /// <summary>
        /// Plural instructor label (the {instructors} placeholder).
        /// </summary>
        public string InstructorLabelPlural { get; set; } = "Instructors";

        /// <summary>
        /// Session label (the {session} placeholder).
        /// </summary>
        public string SessionLabel { get; set; } = "Session";

        /// <summary>
        /// Plural session label (the {sessions} placeholder).
        /// </summary>
        public string SessionLabelPlural { get; set; } = "Sessions";

        /// <summary>
        /// Active (not retired) programs in admin order.
        /// </summary>
        public List<PublicOption> Programs { get; set; } = new List<PublicOption>();

        /// <summary>
        /// Rank profile field key used for instructors, or null.
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
        /// Effective map configuration.
        /// </summary>
        public PublicMapConfig Map { get; set; } = new PublicMapConfig();

        /// <summary>
        /// Whether place search (forward geocoding) is available.
        /// </summary>
        public bool GeocoderConfigured { get; set; } = false;

        /// <summary>
        /// The anonymous public venue listings are served (directory.publicListingsEnabled). Default false.
        /// Clients skip the public routes while this is false.
        /// </summary>
        public bool PublicListingsEnabled { get; set; } = false;

        /// <summary>
        /// Venue review rules (docs/PHASE4_CONTRACT.md section 3.12).
        /// </summary>
        public PublicReviewConfig Reviews { get; set; } = new PublicReviewConfig();

        /// <summary>
        /// Members may add photos and videos to venues (directory.memberMediaEnabled). Default true.
        /// </summary>
        public bool MemberMedia { get; set; } = true;

        #endregion
    }
}
