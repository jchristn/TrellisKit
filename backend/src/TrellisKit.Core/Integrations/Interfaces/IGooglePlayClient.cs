namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Reads and acknowledges Google Play subscriptions and verifies Real-Time Developer Notifications.
    /// Implementations are safe to call concurrently.
    /// </summary>
    public interface IGooglePlayClient
    {
        /// <summary>
        /// Whether the integration has the settings it needs. When false, calls throw <see cref="IntegrationNotConfiguredException"/>.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Read a subscription purchase (purchases.subscriptionsv2.get) using the service account.
        /// </summary>
        /// <param name="purchaseToken">Purchase token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Subscription.</returns>
        /// <exception cref="StoreVerificationException">The token is unknown or the API call failed.</exception>
        /// <exception cref="IntegrationNotConfiguredException">Not configured.</exception>
        Task<GooglePlaySubscription> GetSubscriptionAsync(string purchaseToken, CancellationToken token = default);

        /// <summary>
        /// Acknowledge a subscription purchase (required within 3 days or Google refunds it). Idempotent.
        /// </summary>
        /// <param name="productId">Subscription product identifier.</param>
        /// <param name="purchaseToken">Purchase token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="StoreVerificationException">The API call failed.</exception>
        /// <exception cref="IntegrationNotConfiguredException">Not configured.</exception>
        Task AcknowledgeAsync(string productId, string purchaseToken, CancellationToken token = default);

        /// <summary>
        /// Verify a Pub/Sub push request (Google-signed OIDC token in the Authorization header, audience and service
        /// account email from settings) and decode the developer notification.
        /// </summary>
        /// <param name="body">Request body.</param>
        /// <param name="authorizationHeader">Authorization header value, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Notification.</returns>
        /// <exception cref="WebhookSignatureException">The push token is missing or invalid.</exception>
        /// <exception cref="StoreVerificationException">The body is malformed or for another package.</exception>
        /// <exception cref="IntegrationNotConfiguredException">Not configured.</exception>
        Task<GooglePlayNotification> DecodeNotificationAsync(string body, string? authorizationHeader, CancellationToken token = default);
    }
}
