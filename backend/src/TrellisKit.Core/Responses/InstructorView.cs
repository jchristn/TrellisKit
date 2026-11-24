namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An instructor (docs/PHASE4_CONTRACT.md section 2.3).
    /// </summary>
    public class InstructorView : InstructorSummaryView
    {
        #region Public-Members

        /// <summary>
        /// Bio.
        /// </summary>
        public string? Bio { get; set; } = null;

        /// <summary>
        /// Social links.
        /// </summary>
        public List<SocialLinkView> SocialLinks { get; set; } = new List<SocialLinkView>();

        /// <summary>
        /// Bounded venue preview (at most 10) with the enumeration pointer.
        /// </summary>
        public BoundedPreview<VenueSummaryView>? Venues { get; set; } = null;

        /// <summary>
        /// Staff hint: the linked member's own rank differs from the entry (staff only).
        /// </summary>
        public bool? RankDiffersFromProfile { get; set; } = null;

        #endregion
    }
}
