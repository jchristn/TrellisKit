namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Feedback thread review status (docs/PHASE3_CONTRACT.md section 7).
    /// </summary>
    public enum FeedbackStatusEnum
    {
        /// <summary>
        /// New, waiting for a reviewer.
        /// </summary>
        Open,

        /// <summary>
        /// A reviewer picked it up.
        /// </summary>
        InReview,

        /// <summary>
        /// A review comment was chosen.
        /// </summary>
        Reviewed,

        /// <summary>
        /// Withdrawn by the author or closed by a reviewer.
        /// </summary>
        Closed
    }
}
