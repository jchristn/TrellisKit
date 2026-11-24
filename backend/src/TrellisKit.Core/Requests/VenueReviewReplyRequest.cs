namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// Body of PUT /api/v1/venues/{id}/reviews/{reviewId}/reply (docs/PHASE4_CONTRACT.md section 3.12).
    /// </summary>
    public class VenueReviewReplyRequest
    {
        #region Public-Members

        /// <summary>
        /// Reply text, 1 to directory.reviewReplyMaxLength characters.
        /// </summary>
        public string? Body { get; set; } = null;

        #endregion
    }
}
