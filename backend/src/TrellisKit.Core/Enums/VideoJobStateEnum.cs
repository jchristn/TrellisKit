namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// State of an asynchronous video transcode job.
    /// </summary>
    public enum VideoJobStateEnum
    {
        /// <summary>
        /// Submitted or progressing.
        /// </summary>
        Running,

        /// <summary>
        /// Finished successfully.
        /// </summary>
        Complete,

        /// <summary>
        /// Failed.
        /// </summary>
        Error,

        /// <summary>
        /// Cancelled.
        /// </summary>
        Canceled
    }
}
