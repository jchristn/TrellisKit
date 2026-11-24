namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Lifecycle of a job run.
    /// </summary>
    public enum JobRunStatusEnum
    {
        /// <summary>
        /// Waiting for a worker to plan its items.
        /// </summary>
        Queued,

        /// <summary>
        /// Items are being processed.
        /// </summary>
        Running,

        /// <summary>
        /// Every item succeeded or was skipped.
        /// </summary>
        Succeeded,

        /// <summary>
        /// Some items failed, were dead-lettered, or were skipped by the run budget.
        /// </summary>
        PartiallySucceeded,

        /// <summary>
        /// Planning failed, or every item failed.
        /// </summary>
        Failed,

        /// <summary>
        /// An administrator cancelled the run.
        /// </summary>
        Cancelled,

        /// <summary>
        /// The run was not started (previous run still active, or a budget was reached); SkippedReason says why.
        /// </summary>
        Skipped
    }
}
