namespace TrellisKit.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// Parameters for creating a Stripe Checkout session in subscription mode.
    /// </summary>
    public class StripeCheckoutRequest
    {
        #region Public-Members

        /// <summary>
        /// Member identifier, stored as client_reference_id and subscription metadata.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Existing Stripe customer, or null to create one from Email.
        /// </summary>
        public string? CustomerId { get; set; } = null;

        /// <summary>
        /// Member email, used when creating a customer.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Stripe Price identifier for the annual membership.
        /// </summary>
        public string PriceId { get; set; } = String.Empty;

        /// <summary>
        /// Redirect after successful checkout.
        /// </summary>
        public string SuccessUrl { get; set; } = String.Empty;

        /// <summary>
        /// Redirect when checkout is cancelled.
        /// </summary>
        public string CancelUrl { get; set; } = String.Empty;

        /// <summary>
        /// Coupon applied to the session (discounts[0][coupon]), or null. allow_promotion_codes is omitted when set.
        /// </summary>
        public string? CouponId { get; set; } = null;

        /// <summary>
        /// Discount sign-up code identifier stored in session and subscription metadata (signupCodeId), or null.
        /// </summary>
        public string? SignupCodeId { get; set; } = null;

        #endregion
    }
}
