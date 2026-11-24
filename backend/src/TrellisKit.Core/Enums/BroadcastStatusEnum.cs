namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Broadcast lifecycle status (docs/PHASE5_CONTRACT.md section 2.5).
    /// </summary>
    public enum BroadcastStatusEnum
    {
        /// <summary>
        /// Being written; editable and deletable.
        /// </summary>
        Draft,

        /// <summary>
        /// Waiting for sendAtUtc; editable and cancelable.
        /// </summary>
        Scheduled,

        /// <summary>
        /// The send worker is expanding the segment in batches.
        /// </summary>
        Sending,

        /// <summary>
        /// Every batch was delivered.
        /// </summary>
        Sent,

        /// <summary>
        /// Canceled before or during sending; remaining batches stop.
        /// </summary>
        Canceled
    }
}
