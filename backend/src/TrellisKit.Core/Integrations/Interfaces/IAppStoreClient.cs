namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Verifies StoreKit 2 transactions and App Store Server Notifications V2, and queries the App Store Server API.
    /// Implementations are safe to call concurrently.
    /// </summary>
    public interface IAppStoreClient
    {
        /// <summary>
        /// Whether the integration has the settings it needs. When false, calls throw <see cref="IntegrationNotConfiguredException"/>.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Verify a JWS signed transaction: x5c chain to the Apple Root CA G3, ES256 signature, and bundle id.
        /// </summary>
        /// <param name="signedTransaction">JWS from StoreKit 2.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Transaction.</returns>
        /// <exception cref="StoreVerificationException">Verification failed.</exception>
        /// <exception cref="IntegrationNotConfiguredException">Not configured.</exception>
        Task<AppStoreTransaction> VerifyTransactionAsync(string signedTransaction, CancellationToken token = default);

        /// <summary>
        /// Verify and decode a notification's signedPayload, including the nested signed transaction and renewal info.
        /// </summary>
        /// <param name="signedPayload">signedPayload from the webhook body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Notification.</returns>
        /// <exception cref="StoreVerificationException">Verification failed.</exception>
        /// <exception cref="IntegrationNotConfiguredException">Not configured.</exception>
        Task<AppStoreNotification> DecodeNotificationAsync(string signedPayload, CancellationToken token = default);

        /// <summary>
        /// Read the latest transaction for a subscription from the App Store Server API (Get All Subscription Statuses).
        /// </summary>
        /// <param name="originalTransactionId">Original transaction identifier.</param>
        /// <param name="environment">Environment to query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Latest verified transaction, or null when none exists.</returns>
        /// <exception cref="StoreVerificationException">The API call or verification failed.</exception>
        /// <exception cref="IntegrationNotConfiguredException">Not configured.</exception>
        Task<AppStoreTransaction?> GetLatestTransactionAsync(string originalTransactionId, StoreEnvironmentEnum environment, CancellationToken token = default);
    }
}
