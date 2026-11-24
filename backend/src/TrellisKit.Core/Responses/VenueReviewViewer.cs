namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// What the caller may do with a venue review (docs/PHASE4_CONTRACT.md section 3.12).
    /// </summary>
    public class VenueReviewViewer
    {
        #region Public-Members

        /// <summary>
        /// The caller wrote the review.
        /// </summary>
        public bool IsAuthor { get; set; } = false;

        /// <summary>
        /// The caller may edit it (the author).
        /// </summary>
        public bool CanEdit { get; set; } = false;

        /// <summary>
        /// The caller may delete it (the author).
        /// </summary>
        public bool CanDelete { get; set; } = false;

        /// <summary>
        /// The caller may write or edit the reply (a linked instructor of the venue).
        /// </summary>
        public bool CanReply { get; set; } = false;

        /// <summary>
        /// The caller may delete the reply (its author or directory staff).
        /// </summary>
        public bool CanDeleteReply { get; set; } = false;

        /// <summary>
        /// The caller may report it (signed in, not the author).
        /// </summary>
        public bool CanReport { get; set; } = false;

        /// <summary>
        /// Directory staff may moderate it.
        /// </summary>
        public bool CanModerate { get; set; } = false;

        #endregion
    }
}
