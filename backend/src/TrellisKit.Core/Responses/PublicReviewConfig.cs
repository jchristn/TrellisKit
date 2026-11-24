namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// The review rules clients read from GET /api/v1/property (directory.reviews; docs/PHASE4_CONTRACT.md section 3.12).
    /// </summary>
    public class PublicReviewConfig
    {
        #region Public-Members

        /// <summary>
        /// Reviews are on (directory.reviewsEnabled). Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Shortest review text (directory.reviewMinLength).
        /// </summary>
        public int MinLength { get; set; } = 10;

        /// <summary>
        /// Longest review text (directory.reviewMaxLength).
        /// </summary>
        public int MaxLength { get; set; } = 2000;

        /// <summary>
        /// Longest reply text (directory.reviewReplyMaxLength).
        /// </summary>
        public int ReplyMaxLength { get; set; } = 2000;

        #endregion
    }
}
