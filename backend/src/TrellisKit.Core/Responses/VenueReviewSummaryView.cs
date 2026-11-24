namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The review summary of a venue detail (docs/PHASE4_CONTRACT.md section 3.12).
    /// </summary>
    public class VenueReviewSummaryView
    {
        #region Public-Members

        /// <summary>
        /// Reviews are on for the property (directory.reviewsEnabled).
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Average rating with one decimal, or null without live reviews.
        /// </summary>
        public double? AverageRating { get; set; } = null;

        /// <summary>
        /// Live reviews.
        /// </summary>
        public int Count { get; set; } = 0;

        /// <summary>
        /// Live reviews per star rating, keys "1" to "5".
        /// </summary>
        public Dictionary<string, int> Distribution { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// The caller's own review, or null.
        /// </summary>
        public VenueReviewView? Mine { get; set; } = null;

        /// <summary>
        /// Enumeration path of the reviews.
        /// </summary>
        public string Enumerate { get; set; } = String.Empty;

        #endregion
    }
}
