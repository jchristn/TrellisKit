namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// State of a notification fan-out job.
    /// </summary>
    public enum FanoutStatusEnum
    {
        /// <summary>
        /// Waiting for a worker.
        /// </summary>
        Pending,

        /// <summary>
        /// Claimed by a worker.
        /// </summary>
        Running,

        /// <summary>
        /// Finished.
        /// </summary>
        Done
    }
}
