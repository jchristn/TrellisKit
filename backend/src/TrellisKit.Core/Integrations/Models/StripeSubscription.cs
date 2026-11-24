namespace TrellisKit.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// A Stripe subscription.
    /// </summary>
    public class StripeSubscription
    {
        #region Public-Members

        /// <summary>
        /// Subscription identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Customer identifier.
        /// </summary>
        public string CustomerId { get; set; } = String.Empty;

        /// <summary>
        /// Stripe status: active, trialing, past_due, unpaid, canceled, incomplete, incomplete_expired, paused.
        /// </summary>
        public string Status { get; set; } = String.Empty;

        /// <summary>
        /// Price of the first item, or null.
        /// </summary>
        public string? PriceId { get; set; } = null;

        /// <summary>
        /// Current period start (UTC).
        /// </summary>
        public DateTime? CurrentPeriodStartUtc { get; set; } = null;

        /// <summary>
        /// Current period end (UTC).
        /// </summary>
        public DateTime? CurrentPeriodEndUtc { get; set; } = null;

        /// <summary>
        /// Whether it cancels at the end of the current period.
        /// </summary>
        public bool CancelAtPeriodEnd { get; set; } = false;

        /// <summary>
        /// metadata.userId, or null.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Whether this is a live-mode object.
        /// </summary>
        public bool LiveMode { get; set; } = false;

        /// <summary>
        /// Raw API object (unmanaged JSON).
        /// </summary>
        public string RawPayloadJson { get; set; } = String.Empty;

        #endregion
    }
}
