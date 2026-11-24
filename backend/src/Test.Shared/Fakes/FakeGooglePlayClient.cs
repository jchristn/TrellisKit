namespace Test.Shared.Fakes
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Google Play client backed by in-memory purchases. Notifications are JSON { messageId, purchaseToken, test? } and
    /// verify when the Authorization header is "Bearer valid".
    /// </summary>
    public sealed class FakeGooglePlayClient : IGooglePlayClient
    {
        private readonly ConcurrentDictionary<string, GooglePlaySubscription> _Purchases = new ConcurrentDictionary<string, GooglePlaySubscription>(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _Acknowledged = new ConcurrentQueue<string>();

        /// <summary>
        /// Whether Google Play reports configured. Default true.
        /// </summary>
        public bool Configured { get; set; } = true;

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return Configured; }
        }

        /// <summary>
        /// Acknowledged purchase tokens.
        /// </summary>
        public IReadOnlyList<string> Acknowledged
        {
            get { return _Acknowledged.ToList(); }
        }

        /// <summary>
        /// Add or replace a purchase.
        /// </summary>
        /// <param name="purchase">Purchase.</param>
        public void Put(GooglePlaySubscription purchase)
        {
            _Purchases[purchase.PurchaseToken] = purchase;
        }

        /// <inheritdoc />
        public Task<GooglePlaySubscription> GetSubscriptionAsync(string purchaseToken, CancellationToken token = default)
        {
            if (_Purchases.TryGetValue(purchaseToken, out GooglePlaySubscription? purchase)) return Task.FromResult(purchase);
            throw new StoreVerificationException("Unknown test purchase.");
        }

        /// <inheritdoc />
        public Task AcknowledgeAsync(string productId, string purchaseToken, CancellationToken token = default)
        {
            _Acknowledged.Enqueue(purchaseToken);
            if (_Purchases.TryGetValue(purchaseToken, out GooglePlaySubscription? purchase)) purchase.Acknowledged = true;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<GooglePlayNotification> DecodeNotificationAsync(string body, string? authorizationHeader, CancellationToken token = default)
        {
            if (authorizationHeader != "Bearer valid") throw new WebhookSignatureException("Bad test push token.");
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    JsonElement root = doc.RootElement;
                    return Task.FromResult(new GooglePlayNotification
                    {
                        MessageId = root.GetProperty("messageId").GetString() ?? String.Empty,
                        PurchaseToken = root.TryGetProperty("purchaseToken", out JsonElement t) ? t.GetString() : null,
                        IsTest = root.TryGetProperty("test", out JsonElement test) && test.ValueKind == JsonValueKind.True,
                        NotificationType = 2,
                        RawPayloadJson = body
                    });
                }
            }
            catch (Exception e) when (e is JsonException || e is KeyNotFoundException || e is InvalidOperationException)
            {
                throw new StoreVerificationException("Malformed test notification.", e);
            }
        }
    }
}
