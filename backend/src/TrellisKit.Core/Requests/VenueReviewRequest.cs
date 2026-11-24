namespace TrellisKit.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/venues/{id}/reviews and PUT /api/v1/venues/{id}/reviews/{reviewId} (docs/PHASE4_CONTRACT.md
    /// section 3.12). On PUT, omitted fields stay unchanged.
    /// </summary>
    public class VenueReviewRequest
    {
        #region Public-Members

        /// <summary>
        /// Star rating, a whole number 1 to 5 (required on create).
        /// </summary>
        public int? Rating { get; set; } = null;

        /// <summary>
        /// Review text, directory.reviewMinLength to directory.reviewMaxLength characters (required on create).
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Labels (replace semantics on PUT; null keeps them).
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags (replace semantics on PUT; null keeps them).
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
