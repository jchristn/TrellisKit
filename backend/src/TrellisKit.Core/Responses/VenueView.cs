namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A venue (docs/PHASE4_CONTRACT.md section 2.2).
    /// </summary>
    public class VenueView : VenueSummaryView
    {
        #region Public-Members

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Social links.
        /// </summary>
        public List<SocialLinkView> SocialLinks { get; set; } = new List<SocialLinkView>();

        /// <summary>
        /// Schedule.
        /// </summary>
        public string? ScheduleText { get; set; } = null;

        /// <summary>
        /// Photos (at most directory.maxPhotos).
        /// </summary>
        public List<MediaView> Photos { get; set; } = new List<MediaView>();

        /// <summary>
        /// Bounded instructor preview (at most 10) with the enumeration pointer.
        /// </summary>
        public BoundedPreview<InstructorSummaryView>? Instructors { get; set; } = null;

        /// <summary>
        /// Review summary with the caller's own review (docs/PHASE4_CONTRACT.md section 3.12), or null on public routes and locked stubs.
        /// </summary>
        public VenueReviewSummaryView? Reviews { get; set; } = null;

        #endregion
    }
}
