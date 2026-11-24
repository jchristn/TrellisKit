namespace TrellisKit.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// A verified Stripe webhook event.
    /// </summary>
    public class StripeEvent
    {
        #region Public-Members

        /// <summary>
        /// Event identifier, used for idempotency.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Event type, for example customer.subscription.updated.
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Event creation time (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Subscription the event concerns, or null.
        /// </summary>
        public string? SubscriptionId { get; set; } = null;

        /// <summary>
        /// Customer the event concerns, or null.
        /// </summary>
        public string? CustomerId { get; set; } = null;

        /// <summary>
        /// checkout.session client_reference_id (member id), or null.
        /// </summary>
        public string? ClientReferenceId { get; set; } = null;

        /// <summary>
        /// checkout.session metadata signupCodeId (a discount code used at checkout), or null.
        /// </summary>
        public string? SignupCodeId { get; set; } = null;

        /// <summary>
        /// Whether this is a live-mode event.
        /// </summary>
        public bool LiveMode { get; set; } = false;

        /// <summary>
        /// Raw event payload (unmanaged JSON).
        /// </summary>
        public string RawPayloadJson { get; set; } = String.Empty;

        #endregion
    }
}
