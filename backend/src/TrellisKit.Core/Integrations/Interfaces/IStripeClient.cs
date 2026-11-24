namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Stripe REST client for subscription checkout, the customer portal, subscriptions, and webhook verification.
    /// Implementations are safe to call concurrently.
    /// </summary>
    public interface IStripeClient
    {
        /// <summary>
        /// Whether the integration has the settings it needs. When false, calls throw <see cref="IntegrationNotConfiguredException"/>.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Create a Checkout session in subscription mode.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Session.</returns>
        /// <exception cref="StoreVerificationException">The API call failed.</exception>
        /// <exception cref="IntegrationNotConfiguredException">Not configured.</exception>
        Task<StripeCheckoutSession> CreateCheckoutSessionAsync(StripeCheckoutRequest request, CancellationToken token = default);

        /// <summary>
        /// Create a billing portal session.
        /// </summary>
        /// <param name="customerId">Customer identifier.</param>
        /// <param name="returnUrl">Where the portal returns to.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Portal URL.</returns>
        /// <exception cref="StoreVerificationException">The API call failed.</exception>
        /// <exception cref="IntegrationNotConfiguredException">Not configured.</exception>
        Task<string> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken token = default);

        /// <summary>
        /// Create a coupon (POST /v1/coupons) for a discount sign-up code.
        /// </summary>
        /// <param name="request">Coupon parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Coupon identifier.</returns>
        /// <exception cref="ArgumentException">Neither or both of percent and amount are set.</exception>
        /// <exception cref="StoreVerificationException">The API call failed.</exception>
        /// <exception cref="IntegrationNotConfiguredException">Not configured.</exception>
        Task<string> CreateCouponAsync(StripeCouponRequest request, CancellationToken token = default);

        /// <summary>
        /// Read a subscription.
        /// </summary>
        /// <param name="subscriptionId">Subscription identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Subscription.</returns>
        /// <exception cref="StoreVerificationException">The API call failed.</exception>
        /// <exception cref="IntegrationNotConfiguredException">Not configured.</exception>
        Task<StripeSubscription> GetSubscriptionAsync(string subscriptionId, CancellationToken token = default);

        /// <summary>
        /// Set a subscription to cancel at the end of its current period.
        /// </summary>
        /// <param name="subscriptionId">Subscription identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated subscription.</returns>
        /// <exception cref="StoreVerificationException">The API call failed.</exception>
        /// <exception cref="IntegrationNotConfiguredException">Not configured.</exception>
        Task<StripeSubscription> CancelAtPeriodEndAsync(string subscriptionId, CancellationToken token = default);

        /// <summary>
        /// Verify a webhook (Stripe-Signature header: HMAC-SHA256 over "timestamp.payload" with the webhook secret,
        /// constant-time compare, 300 second tolerance) and parse the event.
        /// </summary>
        /// <param name="payload">Raw request body, exactly as received.</param>
        /// <param name="signatureHeader">Stripe-Signature header value, or null.</param>
        /// <param name="nowUtc">Current time (injectable for tests).</param>
        /// <returns>Event.</returns>
        /// <exception cref="WebhookSignatureException">The signature is missing, invalid, or stale.</exception>
        /// <exception cref="IntegrationNotConfiguredException">Not configured.</exception>
        StripeEvent ParseWebhook(string payload, string? signatureHeader, DateTime nowUtc);
    }
}
