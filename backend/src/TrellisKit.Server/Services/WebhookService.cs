namespace TrellisKit.Server.Services
{
    using System;
    using System.Diagnostics;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Store webhooks (Stripe, App Store Server Notifications V2, Google Play RTDN). Each is signature-verified, stored
    /// raw before processing, and idempotent by store event identifier: a redelivered event that was already processed
    /// or ignored is acknowledged without reprocessing; one that failed is processed again.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class WebhookService
    {
        #region Private-Members

        private const string _Processed = "Processed";
        private const string _Ignored = "Ignored";
        private const string _Failed = "Failed";
        private readonly DatabaseDriverBase _Database;
        private readonly IntegrationSet _Integrations;
        private readonly PurchaseService _Purchases;
        private readonly AppLogger _Logger;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="integrations">Integrations.</param>
        /// <param name="purchases">Purchase service.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public WebhookService(DatabaseDriverBase database, IntegrationSet integrations, PurchaseService purchases, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Integrations = integrations ?? throw new ArgumentNullException(nameof(integrations));
            _Purchases = purchases ?? throw new ArgumentNullException(nameof(purchases));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Handle a Stripe webhook.
        /// </summary>
        /// <param name="rawBody">Body exactly as received.</param>
        /// <param name="signatureHeader">Stripe-Signature header.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Acknowledgement.</returns>
        /// <exception cref="ApiErrorException">400 InvalidSignature, 500 WebhookFailed (Stripe retries), or 503 IntegrationNotConfigured.</exception>
        public async Task<MessageResponse> HandleStripeAsync(string rawBody, string? signatureHeader, CancellationToken token = default)
        {
            using (Activity? activity = TrellisKitTelemetry.StartSpan("webhook.process"))
            {
                activity?.SetTag("store", "Stripe");
                if (!_Integrations.Stripe.IsConfigured) throw Count("Stripe", "not_configured", PurchaseService.NotConfigured(StoreEnum.Stripe));
                StripeEvent stripeEvent;
                try
                {
                    stripeEvent = _Integrations.Stripe.ParseWebhook(rawBody ?? String.Empty, signatureHeader, DateTime.UtcNow);
                }
                catch (WebhookSignatureException e)
                {
                    _Logger.Warn("[WebhookService] Stripe signature rejected: " + e.Message);
                    throw Count("Stripe", "invalid_signature", new ApiErrorException(400, "InvalidSignature", "The webhook signature is invalid."));
                }

                SubscriptionEvent? record = await BeginAsync(StoreEnum.Stripe, stripeEvent.Id, stripeEvent.Type, stripeEvent.RawPayloadJson, token).ConfigureAwait(false);
                if (record == null) return Duplicate("Stripe");

                return await ProcessAsync(record, "Stripe", async () =>
                {
                    User? clientUser = String.IsNullOrEmpty(stripeEvent.ClientReferenceId) ? null : await _Database.Users.ReadAsync(stripeEvent.ClientReferenceId, token).ConfigureAwait(false);
                    if (clientUser != null && !String.IsNullOrEmpty(stripeEvent.CustomerId) && clientUser.StripeCustomerId != stripeEvent.CustomerId)
                    {
                        clientUser.StripeCustomerId = stripeEvent.CustomerId;
                        clientUser.UpdatedUtc = DateTime.UtcNow;
                        await _Database.Users.UpdateAsync(clientUser, token).ConfigureAwait(false);
                    }
                    if (clientUser != null && stripeEvent.Type == "checkout.session.completed" && !String.IsNullOrEmpty(stripeEvent.SignupCodeId) && _Purchases.SignupCodes != null)
                        await _Purchases.SignupCodes.RecordDiscountAsync(stripeEvent.SignupCodeId, clientUser.Id, token).ConfigureAwait(false);
                    if (String.IsNullOrEmpty(stripeEvent.SubscriptionId)) return null;

                    StripeSubscription stripe = await _Integrations.Stripe.GetSubscriptionAsync(stripeEvent.SubscriptionId, token).ConfigureAwait(false);
                    Subscription? existing = await _Database.Subscriptions.ReadByStoreIdAsync(StoreEnum.Stripe, stripe.Id, token).ConfigureAwait(false);
                    string? userId = existing?.UserId;
                    if (userId == null && !String.IsNullOrEmpty(stripe.UserId) && await _Database.Users.ReadAsync(stripe.UserId, token).ConfigureAwait(false) != null) userId = stripe.UserId;
                    if (userId == null) userId = clientUser?.Id;
                    string? customerId = String.IsNullOrEmpty(stripe.CustomerId) ? stripeEvent.CustomerId : stripe.CustomerId;
                    if (userId == null && !String.IsNullOrEmpty(customerId)) userId = (await _Database.Users.ReadByStripeCustomerIdAsync(customerId, token).ConfigureAwait(false))?.Id;
                    if (userId == null) return null;

                    User? owner = await _Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
                    if (owner != null && String.IsNullOrEmpty(owner.StripeCustomerId) && !String.IsNullOrEmpty(customerId))
                    {
                        owner.StripeCustomerId = customerId;
                        owner.UpdatedUtc = DateTime.UtcNow;
                        await _Database.Users.UpdateAsync(owner, token).ConfigureAwait(false);
                    }
                    return await _Purchases.ApplyStripeAsync(stripe, userId, token).ConfigureAwait(false);
                }, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Handle an App Store Server Notification V2.
        /// </summary>
        /// <param name="rawBody">Body exactly as received ({ "signedPayload": "..." }).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Acknowledgement.</returns>
        /// <exception cref="ApiErrorException">400 InvalidSignature, 500 WebhookFailed, or 503 IntegrationNotConfigured.</exception>
        public async Task<MessageResponse> HandleAppleAsync(string rawBody, CancellationToken token = default)
        {
            using (Activity? activity = TrellisKitTelemetry.StartSpan("webhook.process"))
            {
                activity?.SetTag("store", "Apple");
                if (!_Integrations.AppStore.IsConfigured) throw Count("Apple", "not_configured", PurchaseService.NotConfigured(StoreEnum.Apple));
                string? signedPayload = ReadStringProperty(rawBody, "signedPayload");
                if (signedPayload == null) throw Count("Apple", "invalid_body", new ApiErrorException(400, "InvalidSignature", "The body must be { \"signedPayload\": \"...\" }."));

                AppStoreNotification notification;
                try
                {
                    notification = await _Integrations.AppStore.DecodeNotificationAsync(signedPayload, token).ConfigureAwait(false);
                }
                catch (StoreVerificationException e)
                {
                    _Logger.Warn("[WebhookService] Apple notification rejected: " + e.Message);
                    throw Count("Apple", "invalid_signature", new ApiErrorException(400, "InvalidSignature", "The notification could not be verified."));
                }

                string type = notification.NotificationType + (String.IsNullOrEmpty(notification.Subtype) ? String.Empty : "." + notification.Subtype);
                SubscriptionEvent? record = await BeginAsync(StoreEnum.Apple, notification.NotificationUuid, type, String.IsNullOrEmpty(notification.RawPayloadJson) ? rawBody : notification.RawPayloadJson, token).ConfigureAwait(false);
                if (record == null) return Duplicate("Apple");

                return await ProcessAsync(record, "Apple", async () =>
                {
                    AppStoreTransaction? transaction = notification.Transaction;
                    if (transaction == null || String.IsNullOrEmpty(transaction.OriginalTransactionId)) return null;
                    Subscription? existing = await _Database.Subscriptions.ReadByStoreIdAsync(StoreEnum.Apple, transaction.OriginalTransactionId, token).ConfigureAwait(false);
                    string? userId = existing?.UserId;
                    if (userId == null && transaction.AppAccountToken != null)
                        userId = (await _Database.Users.ReadByStoreAccountTokenAsync(transaction.AppAccountToken.Value.ToString("D"), token).ConfigureAwait(false))?.Id;
                    if (userId == null) return null;
                    return await _Purchases.ApplyAppleAsync(transaction, userId, AppleStatus(notification), notification.AutoRenewEnabled, token).ConfigureAwait(false);
                }, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Handle a Google Play Real-Time Developer Notification (Pub/Sub push).
        /// </summary>
        /// <param name="rawBody">Body exactly as received.</param>
        /// <param name="authorizationHeader">Authorization header (Pub/Sub OIDC token).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Acknowledgement.</returns>
        /// <exception cref="ApiErrorException">401 InvalidSignature, 400 InvalidNotification, 500 WebhookFailed, or 503 IntegrationNotConfigured.</exception>
        public async Task<MessageResponse> HandleGoogleAsync(string rawBody, string? authorizationHeader, CancellationToken token = default)
        {
            using (Activity? activity = TrellisKitTelemetry.StartSpan("webhook.process"))
            {
                activity?.SetTag("store", "Google");
                if (!_Integrations.GooglePlay.IsConfigured) throw Count("Google", "not_configured", PurchaseService.NotConfigured(StoreEnum.Google));
                GooglePlayNotification notification;
                try
                {
                    notification = await _Integrations.GooglePlay.DecodeNotificationAsync(rawBody ?? String.Empty, authorizationHeader, token).ConfigureAwait(false);
                }
                catch (WebhookSignatureException e)
                {
                    _Logger.Warn("[WebhookService] Google push rejected: " + e.Message);
                    throw Count("Google", "invalid_signature", new ApiErrorException(401, "InvalidSignature", "The push authentication token is invalid."));
                }
                catch (StoreVerificationException e)
                {
                    _Logger.Warn("[WebhookService] Google notification rejected: " + e.Message);
                    throw Count("Google", "invalid_body", new ApiErrorException(400, "InvalidNotification", "The notification is malformed."));
                }

                string type = notification.IsTest ? "TEST" : "SUBSCRIPTION_" + notification.NotificationType;
                SubscriptionEvent? record = await BeginAsync(StoreEnum.Google, notification.MessageId, type, String.IsNullOrEmpty(notification.RawPayloadJson) ? rawBody : notification.RawPayloadJson, token).ConfigureAwait(false);
                if (record == null) return Duplicate("Google");

                return await ProcessAsync(record, "Google", async () =>
                {
                    if (notification.IsTest || String.IsNullOrEmpty(notification.PurchaseToken)) return null;
                    GooglePlaySubscription purchase = await _Integrations.GooglePlay.GetSubscriptionAsync(notification.PurchaseToken, token).ConfigureAwait(false);
                    purchase.PurchaseToken = notification.PurchaseToken;
                    Subscription? existing = await _Database.Subscriptions.ReadByStoreIdAsync(StoreEnum.Google, notification.PurchaseToken, token).ConfigureAwait(false);
                    string? userId = existing?.UserId;
                    if (userId == null && !String.IsNullOrEmpty(purchase.ObfuscatedAccountId))
                        userId = (await _Database.Users.ReadByStoreAccountTokenAsync(purchase.ObfuscatedAccountId, token).ConfigureAwait(false))?.Id;
                    if (userId == null) return null;
                    return await _Purchases.ApplyGoogleAsync(purchase, userId, token).ConfigureAwait(false);
                }, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Status implied by an App Store notification type, or null to derive it from the transaction.
        /// </summary>
        /// <param name="notification">Notification.</param>
        /// <returns>Status, or null.</returns>
        public static SubscriptionStatusEnum? AppleStatus(AppStoreNotification notification)
        {
            ArgumentNullException.ThrowIfNull(notification);
            switch (notification.NotificationType)
            {
                case "REFUND":
                    return SubscriptionStatusEnum.Refunded;
                case "REVOKE":
                    return SubscriptionStatusEnum.Revoked;
                case "EXPIRED":
                case "GRACE_PERIOD_EXPIRED":
                    return SubscriptionStatusEnum.Expired;
                case "DID_FAIL_TO_RENEW":
                    return notification.Subtype == "GRACE_PERIOD" ? SubscriptionStatusEnum.InGrace : SubscriptionStatusEnum.InBillingRetry;
                default:
                    return null;
            }
        }

        #endregion

        #region Private-Methods

        private async Task<SubscriptionEvent?> BeginAsync(StoreEnum store, string storeEventId, string eventType, string? raw, CancellationToken token)
        {
            if (String.IsNullOrEmpty(storeEventId)) storeEventId = "missing:" + IdGenerator.GenerateSubscriptionEventId();
            SubscriptionEvent record = new SubscriptionEvent
            {
                Id = IdGenerator.GenerateSubscriptionEventId(),
                Store = store,
                StoreEventId = storeEventId,
                Source = SubscriptionEventSourceEnum.Webhook,
                EventType = String.IsNullOrEmpty(eventType) ? "unknown" : eventType,
                ReceivedUtc = DateTime.UtcNow,
                Outcome = "Received",
                RawPayloadJson = raw
            };
            if (await _Database.SubscriptionEvents.TryCreateAsync(record, token).ConfigureAwait(false)) return record;
            SubscriptionEvent? existing = await _Database.SubscriptionEvents.ReadByStoreEventIdAsync(store, storeEventId, token).ConfigureAwait(false);
            if (existing == null || existing.Outcome == _Processed || existing.Outcome == _Ignored) return null;
            return existing;
        }

        private async Task<MessageResponse> ProcessAsync(SubscriptionEvent record, string store, Func<Task<Subscription?>> work, CancellationToken token)
        {
            try
            {
                Subscription? subscription = await work().ConfigureAwait(false);
                record.SubscriptionId = subscription?.Id;
                record.UserId = subscription?.UserId;
                record.Outcome = subscription == null ? _Ignored : _Processed;
                record.ProcessedUtc = DateTime.UtcNow;
                await _Database.SubscriptionEvents.UpdateAsync(record, token).ConfigureAwait(false);
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.Webhooks, "store", store, "outcome", record.Outcome.ToLowerInvariant());
                return new MessageResponse(record.Outcome + ".");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logger.Error("[WebhookService] " + store + " event " + record.StoreEventId + " failed", e);
                record.Outcome = _Failed;
                record.ProcessedUtc = DateTime.UtcNow;
                try
                {
                    await _Database.SubscriptionEvents.UpdateAsync(record, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception inner) when (inner is not OperationCanceledException)
                {
                    _Logger.Warn("[WebhookService] could not record the failure: " + inner.Message);
                }
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.Webhooks, "store", store, "outcome", "failed");
                throw new ApiErrorException(500, "WebhookFailed", "The event could not be processed and will be retried.");
            }
        }

        private static MessageResponse Duplicate(string store)
        {
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.Webhooks, "store", store, "outcome", "duplicate");
            return new MessageResponse("Duplicate.");
        }

        private static ApiErrorException Count(string store, string outcome, ApiErrorException error)
        {
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.Webhooks, "store", store, "outcome", outcome);
            return error;
        }

        private static string? ReadStringProperty(string? json, string name)
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                        return value.GetString();
                }
            }
            catch (JsonException)
            {
                return null;
            }
            return null;
        }

        #endregion
    }
}
