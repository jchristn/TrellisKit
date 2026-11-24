namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Normalized subscription status across stores.
    /// </summary>
    public enum SubscriptionStatusEnum
    {
        /// <summary>
        /// Paid and current.
        /// </summary>
        Active,

        /// <summary>
        /// Payment failed; store grace period, still entitled.
        /// </summary>
        InGrace,

        /// <summary>
        /// Payment failed; store retrying, not entitled.
        /// </summary>
        InBillingRetry,

        /// <summary>
        /// Paused by the member (Google Play).
        /// </summary>
        Paused,

        /// <summary>
        /// Ended without renewal.
        /// </summary>
        Expired,

        /// <summary>
        /// Access revoked by the store or support.
        /// </summary>
        Revoked,

        /// <summary>
        /// Refunded; not entitled.
        /// </summary>
        Refunded
    }
}
