namespace TrellisKit.Core.Helpers
{
    /// <summary>
    /// Identifier prefixes of the Phase 4 directory objects (docs/PHASE4_CONTRACT.md section 0.1).
    /// </summary>
    public static class DirectoryIdPrefixes
    {
        #region Public-Members

        /// <summary>
        /// Venue identifier prefix.
        /// </summary>
        public const string Venue = "ven_";

        /// <summary>
        /// Instructor identifier prefix.
        /// </summary>
        public const string Instructor = "ins_";

        /// <summary>
        /// Venue to instructor link identifier prefix.
        /// </summary>
        public const string VenueInstructor = "vin_";

        /// <summary>
        /// Program option identifier prefix.
        /// </summary>
        public const string Program = "dpg_";

        /// <summary>
        /// Submission identifier prefix.
        /// </summary>
        public const string Submission = "dsb_";

        /// <summary>
        /// Submission field row identifier prefix.
        /// </summary>
        public const string SubmissionField = "dsf_";

        /// <summary>
        /// Geocoder cache entry identifier prefix.
        /// </summary>
        public const string GeocodeResult = "gcr_";

        /// <summary>
        /// Social link identifier prefix.
        /// </summary>
        public const string SocialLink = "dsl_";

        /// <summary>
        /// Venue review identifier prefix.
        /// </summary>
        public const string Review = "vrv_";

        /// <summary>
        /// Venue member media item identifier prefix.
        /// </summary>
        public const string VenueMedia = "vmd_";

        #endregion
    }
}
