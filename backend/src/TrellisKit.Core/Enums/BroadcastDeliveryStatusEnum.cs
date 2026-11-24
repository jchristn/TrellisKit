namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Status of one channel of one broadcast delivery.
    /// </summary>
    public enum BroadcastDeliveryStatusEnum
    {
        /// <summary>
        /// The broadcast does not use the channel.
        /// </summary>
        NotRequested,

        /// <summary>
        /// Accepted for delivery; the worker has not finished the channel yet.
        /// </summary>
        Pending,

        /// <summary>
        /// The in-app notification exists.
        /// </summary>
        Created,

        /// <summary>
        /// Handed to the push or email pipeline.
        /// </summary>
        Queued,

        /// <summary>
        /// Sent by the provider.
        /// </summary>
        Sent,

        /// <summary>
        /// Delivered (email delivery webhook).
        /// </summary>
        Delivered,

        /// <summary>
        /// Failed permanently.
        /// </summary>
        Failed,

        /// <summary>
        /// Suppressed (email suppression list, or push pause, notification hours, or rate).
        /// </summary>
        Suppressed,

        /// <summary>
        /// The member turned the Announcements channel off.
        /// </summary>
        SkippedPreference,

        /// <summary>
        /// No active push token.
        /// </summary>
        NoToken,

        /// <summary>
        /// No verified email address.
        /// </summary>
        NoAddress
    }
}
