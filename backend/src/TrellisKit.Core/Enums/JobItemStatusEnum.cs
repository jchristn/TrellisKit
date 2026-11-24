namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Lifecycle of one job item.
    /// </summary>
    public enum JobItemStatusEnum
    {
        /// <summary>
        /// Waiting for its next attempt.
        /// </summary>
        Pending,

        /// <summary>
        /// Claimed by a worker.
        /// </summary>
        Running,

        /// <summary>
        /// Finished successfully.
        /// </summary>
        Succeeded,

        /// <summary>
        /// The last attempt failed and no retry remains for a non-retryable error.
        /// </summary>
        Failed,

        /// <summary>
        /// Every attempt failed; kept in the dead-letter view until retried or pruned.
        /// </summary>
        Dead,

        /// <summary>
        /// Not executed (run cancelled or run budget reached).
        /// </summary>
        Skipped
    }
}
