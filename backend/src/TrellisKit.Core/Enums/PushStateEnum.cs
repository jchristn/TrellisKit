namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Push delivery state of a notification.
    /// </summary>
    public enum PushStateEnum
    {
        /// <summary>
        /// No push is due.
        /// </summary>
        None,

        /// <summary>
        /// Queued for the push worker.
        /// </summary>
        Pending,

        /// <summary>
        /// Sent to at least one device.
        /// </summary>
        Sent,

        /// <summary>
        /// Not sent (preference, pause, hours, rate, or no device).
        /// </summary>
        Suppressed,

        /// <summary>
        /// Every send attempt failed.
        /// </summary>
        Failed
    }
}
