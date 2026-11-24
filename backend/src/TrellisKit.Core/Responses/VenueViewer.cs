namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// What the caller may do with a venue (docs/PHASE4_CONTRACT.md section 1.3).
    /// </summary>
    public class VenueViewer
    {
        #region Public-Members

        /// <summary>
        /// Directory staff may edit.
        /// </summary>
        public bool CanEdit { get; set; } = false;

        /// <summary>
        /// The caller may submit an EditVenue suggestion.
        /// </summary>
        public bool CanSuggestEdit { get; set; } = false;

        /// <summary>
        /// The caller may submit a ReportClosed suggestion.
        /// </summary>
        public bool CanReportClosed { get; set; } = false;

        /// <summary>
        /// The caller may report it.
        /// </summary>
        public bool CanReport { get; set; } = false;

        /// <summary>
        /// Directory staff may moderate it.
        /// </summary>
        public bool CanModerate { get; set; } = false;

        /// <summary>
        /// The caller may write a review (reviews on, may make directory writes, no live review yet).
        /// </summary>
        public bool CanReview { get; set; } = false;

        /// <summary>
        /// The caller may add photos and videos (member media on, may make directory writes and post media).
        /// </summary>
        public bool CanAddMedia { get; set; } = false;

        /// <summary>
        /// The caller may remove any member media item (directory staff or a linked instructor of the venue).
        /// </summary>
        public bool CanManageMedia { get; set; } = false;

        /// <summary>
        /// The caller may reply to reviews (a linked instructor of the venue).
        /// </summary>
        public bool CanReplyToReviews { get; set; } = false;

        /// <summary>
        /// False on a locked stub.
        /// </summary>
        public bool CanOpen { get; set; } = true;

        /// <summary>
        /// SignIn or Membership on a locked stub, else null.
        /// </summary>
        public string? Requires { get; set; } = null;

        #endregion
    }
}
