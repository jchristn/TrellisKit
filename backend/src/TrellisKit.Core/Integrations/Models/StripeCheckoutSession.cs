namespace TrellisKit.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// A created Stripe Checkout session.
    /// </summary>
    public class StripeCheckoutSession
    {
        #region Public-Members

        /// <summary>
        /// Session identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Hosted checkout URL to redirect the member to.
        /// </summary>
        public string Url { get; set; } = String.Empty;

        #endregion
    }
}
