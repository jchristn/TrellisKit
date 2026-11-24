namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Status of an administrative email record.
    /// </summary>
    public enum EmailStatusEnum
    {
        /// <summary>
        /// Waiting for delivery (first attempt or retry).
        /// </summary>
        Queued,

        /// <summary>
        /// Accepted by the provider (or written to the log).
        /// </summary>
        Sent,

        /// <summary>
        /// Delivery confirmed by a provider webhook.
        /// </summary>
        Delivered,

        /// <summary>
        /// Delivery failed permanently or every attempt failed.
        /// </summary>
        Failed,

        /// <summary>
        /// Not sent: the address is suppressed.
        /// </summary>
        Suppressed
    }
}
