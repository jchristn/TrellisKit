namespace Test.Shared.Fakes
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// App Store client that verifies scripted signed transactions and notifications.
    /// </summary>
    public sealed class FakeAppStoreClient : IAppStoreClient
    {
        private readonly ConcurrentDictionary<string, AppStoreTransaction> _Transactions = new ConcurrentDictionary<string, AppStoreTransaction>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, AppStoreNotification> _Notifications = new ConcurrentDictionary<string, AppStoreNotification>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, AppStoreTransaction> _Latest = new ConcurrentDictionary<string, AppStoreTransaction>(StringComparer.Ordinal);

        /// <summary>
        /// Whether the App Store reports configured. Default true.
        /// </summary>
        public bool Configured { get; set; } = true;

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return Configured; }
        }

        /// <summary>
        /// Script a signed transaction.
        /// </summary>
        /// <param name="jws">Signed transaction text.</param>
        /// <param name="transaction">Transaction it verifies to.</param>
        public void PutTransaction(string jws, AppStoreTransaction transaction)
        {
            _Transactions[jws] = transaction;
        }

        /// <summary>
        /// Script a notification.
        /// </summary>
        /// <param name="signedPayload">Signed payload text.</param>
        /// <param name="notification">Notification it decodes to.</param>
        public void PutNotification(string signedPayload, AppStoreNotification notification)
        {
            _Notifications[signedPayload] = notification;
        }

        /// <summary>
        /// Script the latest transaction returned by the App Store Server API.
        /// </summary>
        /// <param name="transaction">Transaction.</param>
        public void PutLatest(AppStoreTransaction transaction)
        {
            _Latest[transaction.OriginalTransactionId] = transaction;
        }

        /// <inheritdoc />
        public Task<AppStoreTransaction> VerifyTransactionAsync(string signedTransaction, CancellationToken token = default)
        {
            if (_Transactions.TryGetValue(signedTransaction, out AppStoreTransaction? transaction)) return Task.FromResult(transaction);
            throw new StoreVerificationException("Unknown test transaction.");
        }

        /// <inheritdoc />
        public Task<AppStoreNotification> DecodeNotificationAsync(string signedPayload, CancellationToken token = default)
        {
            if (_Notifications.TryGetValue(signedPayload, out AppStoreNotification? notification)) return Task.FromResult(notification);
            throw new StoreVerificationException("Unknown test notification.");
        }

        /// <inheritdoc />
        public Task<AppStoreTransaction?> GetLatestTransactionAsync(string originalTransactionId, StoreEnvironmentEnum environment, CancellationToken token = default)
        {
            _Latest.TryGetValue(originalTransactionId, out AppStoreTransaction? transaction);
            return Task.FromResult(transaction);
        }
    }
}
