namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// What started a job run.
    /// </summary>
    public enum JobTriggerEnum
    {
        /// <summary>
        /// The definition's schedule.
        /// </summary>
        Schedule,

        /// <summary>
        /// An administrator ran it now.
        /// </summary>
        Manual,

        /// <summary>
        /// An administrator ran it as a dry run (handlers must not write lasting changes).
        /// </summary>
        DryRun
    }
}
