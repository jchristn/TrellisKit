namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Outcome of one model call in usage rollups.
    /// </summary>
    public enum AiUsageOutcomeEnum
    {
        /// <summary>
        /// Succeeded.
        /// </summary>
        Ok,

        /// <summary>
        /// Failed.
        /// </summary>
        Error,

        /// <summary>
        /// Timed out.
        /// </summary>
        Timeout,

        /// <summary>
        /// Rejected by the endpoint queue.
        /// </summary>
        Busy,

        /// <summary>
        /// No healthy endpoint.
        /// </summary>
        Unavailable,

        /// <summary>
        /// Blocked by moderation.
        /// </summary>
        Blocked,

        /// <summary>
        /// Cancelled.
        /// </summary>
        Cancelled
    }
}
