namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
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
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Store purchases: Stripe Checkout and the customer portal, App Store and Google Play verification, and the shared
    /// path that maps a store's view of a subscription onto the subscription row and its entitlement.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class PurchaseService
    {
        #region Public-Members

        /// <summary>
        /// Administrative email (billing lifecycle messages). Set by the composition root; null sends none.
        /// </summary>
        public EmailService? Email { get; set; } = null;

        /// <summary>
        /// Sign-up codes (discount codes at checkout and their redemption records). Set by the composition root; null
        /// refuses checkout codes.
        /// </summary>
        public SignupCodeService? SignupCodes { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly IntegrationSet _Integrations;
        private readonly AppSettings _Settings;
        private readonly PropertyContext _Property;
        private readonly MembershipService _Membership;
        private readonly TokenService _Tokens;
        private readonly AppLogger _Logger;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="integrations">Integrations.</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="property">Active property context (website URL and store product ids).</param>
        /// <param name="membership">Membership service.</param>
        /// <param name="tokens">Token service (for cache invalidation).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PurchaseService(DatabaseDriverBase database, IntegrationSet integrations, AppSettings settings, PropertyContext property, MembershipService membership, TokenService tokens, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Integrations = integrations ?? throw new ArgumentNullException(nameof(integrations));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Property = property ?? throw new ArgumentNullException(nameof(property));
            _Membership = membership ?? throw new ArgumentNullException(nameof(membership));
            _Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a Stripe Checkout session for the annual membership.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Checkout URL.</returns>
        /// <exception cref="ApiErrorException">409 AlreadyMember, 503 IntegrationNotConfigured, or 502 StoreError.</exception>
        public Task<UrlResponse> CheckoutAsync(string userId, CancellationToken token = default)
        {
            return CheckoutAsync(userId, null, null, token);
        }

        /// <summary>
        /// Create a Stripe Checkout session for the annual membership, optionally with a discount sign-up code. The code's
        /// Stripe coupon is created on its first checkout and reused after that; the redemption is recorded when Stripe
        /// reports the checkout complete (<see cref="SignupCodeService.RecordDiscountAsync"/>).
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="code">Discount code as entered, or null.</param>
        /// <param name="info">Request info (rate limiting of codes); required when a code is given.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Checkout URL.</returns>
        /// <exception cref="ApiErrorException">400 InvalidCode, 409 AlreadyMember, 429 RateLimited, 503 IntegrationNotConfigured, or 502 StoreError.</exception>
        public async Task<UrlResponse> CheckoutAsync(string userId, string? code, RequestInfo? info, CancellationToken token = default)
        {
            RequireStripe();
            User user = await RequireUserAsync(userId, token).ConfigureAwait(false);
            MembershipState state = await _Membership.GetStateAsync(user.Id, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            if (state.Entitlements.Any(e => e.Source == MembershipSourceEnum.Subscription && e.IsActiveAt(now)))
                throw new ApiErrorException(409, "AlreadyMember", "You already have an active paid membership.");

            SignupCode? discount = null;
            if (!String.IsNullOrWhiteSpace(code))
            {
                if (SignupCodes == null || info == null) throw new ApiErrorException(400, "InvalidCode", SignupCodeService.InvalidMessage);
                discount = await SignupCodes.RequireDiscountForCheckoutAsync(user.Id, code, info, token).ConfigureAwait(false);
            }

            string baseUrl = _Property.PublicBaseUrl;
            try
            {
                if (discount != null && String.IsNullOrEmpty(discount.StripeCouponId) && SignupCodes != null)
                {
                    string couponId = await _Integrations.Stripe.CreateCouponAsync(new StripeCouponRequest
                    {
                        Name = discount.Name,
                        PercentOff = discount.DiscountPercent,
                        AmountOffCents = discount.DiscountPercent == null ? discount.DiscountAmountCents : null,
                        Currency = "usd",
                        Forever = discount.DiscountDuration == DiscountDurationEnum.Forever,
                        SignupCodeId = discount.Id
                    }, token).ConfigureAwait(false);
                    await SignupCodes.SaveCouponAsync(discount, couponId, token).ConfigureAwait(false);
                }
                StripeCheckoutSession session = await _Integrations.Stripe.CreateCheckoutSessionAsync(new StripeCheckoutRequest
                {
                    CouponId = discount?.StripeCouponId,
                    SignupCodeId = discount?.Id,
                    UserId = user.Id,
                    CustomerId = user.StripeCustomerId,
                    Email = user.Email,
                    PriceId = _Settings.Stripe.PriceId,
                    SuccessUrl = baseUrl + "/app/membership?checkout=success",
                    CancelUrl = baseUrl + "/app/membership?checkout=cancelled"
                }, token).ConfigureAwait(false);
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.Purchases, "store", "Stripe", "outcome", "checkout_started");
                return new UrlResponse { Url = session.Url };
            }
            catch (StoreVerificationException e)
            {
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.Purchases, "store", "Stripe", "outcome", "checkout_failed");
                throw StoreError("Stripe", e);
            }
        }

        /// <summary>
        /// Create a Stripe billing portal session.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Portal URL.</returns>
        /// <exception cref="ApiErrorException">404 NoStripeCustomer, 503 IntegrationNotConfigured, or 502 StoreError.</exception>
        public async Task<UrlResponse> PortalAsync(string userId, CancellationToken token = default)
        {
            RequireStripe();
            User user = await RequireUserAsync(userId, token).ConfigureAwait(false);
            if (String.IsNullOrEmpty(user.StripeCustomerId)) throw new ApiErrorException(404, "NoStripeCustomer", "You have no web subscription to manage.");
            try
            {
                string url = await _Integrations.Stripe.CreatePortalSessionAsync(user.StripeCustomerId, _Property.Link("/app/membership"), token).ConfigureAwait(false);
                return new UrlResponse { Url = url };
            }
            catch (StoreVerificationException e)
            {
                throw StoreError("Stripe", e);
            }
        }

        /// <summary>
        /// Verify a StoreKit 2 signed transaction and grant membership.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="signedTransaction">JWS.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Membership.</returns>
        /// <exception cref="ApiErrorException">400 InvalidTransaction, 409 TransactionBelongsToAnotherAccount, or 503 IntegrationNotConfigured.</exception>
        public async Task<MembershipEnvelope> VerifyAppleAsync(string userId, string? signedTransaction, CancellationToken token = default)
        {
            using (Activity? activity = TrellisKitTelemetry.StartSpan("membership.verify"))
            {
                activity?.SetTag("store", "Apple");
                if (!_Integrations.AppStore.IsConfigured) throw Verification("Apple", "not_configured", NotConfigured(StoreEnum.Apple));
                User user = await RequireUserAsync(userId, token).ConfigureAwait(false);
                if (String.IsNullOrWhiteSpace(signedTransaction)) throw Verification("Apple", "invalid", ApiErrorException.Field("signedTransaction", "A signed transaction is required."));

                AppStoreTransaction transaction;
                try
                {
                    transaction = await _Integrations.AppStore.VerifyTransactionAsync(signedTransaction.Trim(), token).ConfigureAwait(false);
                }
                catch (StoreVerificationException e)
                {
                    _Logger.Debug("[PurchaseService] Apple transaction rejected: " + e.Message);
                    throw Verification("Apple", "invalid", new ApiErrorException(400, "InvalidTransaction", "The App Store transaction could not be verified."));
                }
                catch (IntegrationNotConfiguredException)
                {
                    throw Verification("Apple", "not_configured", NotConfigured(StoreEnum.Apple));
                }

                if (!String.Equals(transaction.ProductId, _Property.AppStoreProductId, StringComparison.Ordinal))
                    throw Verification("Apple", "wrong_product", new ApiErrorException(400, "InvalidTransaction", "The transaction is for another product."));
                if (transaction.AppAccountToken != null && !String.Equals(transaction.AppAccountToken.Value.ToString("D"), user.StoreAccountToken, StringComparison.OrdinalIgnoreCase))
                    throw Verification("Apple", "other_account", BelongsToAnother());

                Subscription? existing = await _Database.Subscriptions.ReadByStoreIdAsync(StoreEnum.Apple, transaction.OriginalTransactionId, token).ConfigureAwait(false);
                if (existing != null && existing.UserId != user.Id) throw Verification("Apple", "other_account", BelongsToAnother());

                Subscription subscription = await ApplyAppleAsync(transaction, user.Id, null, null, token).ConfigureAwait(false);
                await RecordEventAsync(subscription, StoreEnum.Apple, SubscriptionEventSourceEnum.Verify, "VERIFY", transaction.RawPayloadJson, token).ConfigureAwait(false);
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.Verifications, "store", "Apple", "outcome", "success");
                return new MembershipEnvelope { Membership = await _Membership.GetSummaryAsync(user.Id, token).ConfigureAwait(false) };
            }
        }

        /// <summary>
        /// Verify (and acknowledge) a Google Play purchase and grant membership.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="purchaseToken">Purchase token.</param>
        /// <param name="productId">Product identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Membership.</returns>
        /// <exception cref="ApiErrorException">400 InvalidPurchase, 409 TransactionBelongsToAnotherAccount, or 503 IntegrationNotConfigured.</exception>
        public async Task<MembershipEnvelope> VerifyGoogleAsync(string userId, string? purchaseToken, string? productId, CancellationToken token = default)
        {
            using (Activity? activity = TrellisKitTelemetry.StartSpan("membership.verify"))
            {
                activity?.SetTag("store", "Google");
                if (!_Integrations.GooglePlay.IsConfigured) throw Verification("Google", "not_configured", NotConfigured(StoreEnum.Google));
                User user = await RequireUserAsync(userId, token).ConfigureAwait(false);
                if (String.IsNullOrWhiteSpace(purchaseToken)) throw Verification("Google", "invalid", ApiErrorException.Field("purchaseToken", "A purchase token is required."));
                string expectedProduct = _Property.GooglePlayProductId;
                if (!String.IsNullOrWhiteSpace(productId) && !String.Equals(productId.Trim(), expectedProduct, StringComparison.Ordinal))
                    throw Verification("Google", "wrong_product", new ApiErrorException(400, "InvalidPurchase", "The purchase is for another product."));

                GooglePlaySubscription purchase;
                try
                {
                    purchase = await _Integrations.GooglePlay.GetSubscriptionAsync(purchaseToken.Trim(), token).ConfigureAwait(false);
                }
                catch (StoreVerificationException e)
                {
                    _Logger.Debug("[PurchaseService] Google purchase rejected: " + e.Message);
                    throw Verification("Google", "invalid", new ApiErrorException(400, "InvalidPurchase", "The Google Play purchase could not be verified."));
                }
                catch (IntegrationNotConfiguredException)
                {
                    throw Verification("Google", "not_configured", NotConfigured(StoreEnum.Google));
                }

                if (!String.IsNullOrEmpty(purchase.ProductId) && !String.Equals(purchase.ProductId, expectedProduct, StringComparison.Ordinal))
                    throw Verification("Google", "wrong_product", new ApiErrorException(400, "InvalidPurchase", "The purchase is for another product."));
                if (!String.IsNullOrEmpty(purchase.ObfuscatedAccountId) && !String.Equals(purchase.ObfuscatedAccountId, user.StoreAccountToken, StringComparison.OrdinalIgnoreCase))
                    throw Verification("Google", "other_account", BelongsToAnother());

                string storeId = String.IsNullOrEmpty(purchase.PurchaseToken) ? purchaseToken.Trim() : purchase.PurchaseToken;
                purchase.PurchaseToken = storeId;
                Subscription? existing = await _Database.Subscriptions.ReadByStoreIdAsync(StoreEnum.Google, storeId, token).ConfigureAwait(false);
                if (existing != null && existing.UserId != user.Id) throw Verification("Google", "other_account", BelongsToAnother());

                if (!purchase.Acknowledged && (purchase.State == GooglePlaySubscriptionStateEnum.Active || purchase.State == GooglePlaySubscriptionStateEnum.InGracePeriod))
                {
                    try
                    {
                        await _Integrations.GooglePlay.AcknowledgeAsync(String.IsNullOrEmpty(purchase.ProductId) ? expectedProduct : purchase.ProductId, storeId, token).ConfigureAwait(false);
                    }
                    catch (StoreVerificationException e)
                    {
                        _Logger.Warn("[PurchaseService] Google acknowledge failed (will retry on the next verification or reconciliation): " + e.Message);
                    }
                }

                Subscription subscription = await ApplyGoogleAsync(purchase, user.Id, token).ConfigureAwait(false);
                await RecordEventAsync(subscription, StoreEnum.Google, SubscriptionEventSourceEnum.Verify, "VERIFY", purchase.RawPayloadJson, token).ConfigureAwait(false);
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.Verifications, "store", "Google", "outcome", "success");
                return new MembershipEnvelope { Membership = await _Membership.GetSummaryAsync(user.Id, token).ConfigureAwait(false) };
            }
        }

        /// <summary>
        /// Apply an App Store transaction to its subscription and entitlement.
        /// </summary>
        /// <param name="transaction">Verified transaction.</param>
        /// <param name="userId">Owning member.</param>
        /// <param name="statusOverride">Status from a notification type, or null to derive it from the transaction.</param>
        /// <param name="autoRenew">Auto-renew from renewal info, or null to keep the current value.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Subscription.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public Task<Subscription> ApplyAppleAsync(AppStoreTransaction transaction, string userId, SubscriptionStatusEnum? statusOverride, bool? autoRenew, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(transaction);
            DateTime now = DateTime.UtcNow;
            SubscriptionStatusEnum status = statusOverride ?? (transaction.RevocationUtc != null ? SubscriptionStatusEnum.Revoked
                : transaction.ExpiresUtc != null && transaction.ExpiresUtc.Value > now ? SubscriptionStatusEnum.Active : SubscriptionStatusEnum.Expired);
            return UpsertAsync(StoreEnum.Apple, transaction.OriginalTransactionId, userId, transaction.ProductId, status, transaction.PurchaseUtc == DateTime.MinValue ? null : transaction.PurchaseUtc,
                transaction.ExpiresUtc, autoRenew, transaction.Environment, transaction.TransactionId, token);
        }

        /// <summary>
        /// Apply a Google Play subscription to its subscription row and entitlement.
        /// </summary>
        /// <param name="purchase">Subscription from the Play Developer API.</param>
        /// <param name="userId">Owning member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Subscription.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<Subscription> ApplyGoogleAsync(GooglePlaySubscription purchase, string userId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(purchase);
            if (!String.IsNullOrEmpty(purchase.LinkedPurchaseToken))
            {
                Subscription? replaced = await _Database.Subscriptions.ReadByStoreIdAsync(StoreEnum.Google, purchase.LinkedPurchaseToken, token).ConfigureAwait(false);
                if (replaced != null && replaced.Status != SubscriptionStatusEnum.Expired)
                {
                    replaced.Status = SubscriptionStatusEnum.Expired;
                    replaced.AutoRenew = false;
                    replaced.UpdatedUtc = DateTime.UtcNow;
                    replaced.LastEventUtc = DateTime.UtcNow;
                    await _Database.Subscriptions.UpdateAsync(replaced, token).ConfigureAwait(false);
                    await SyncEntitlementAsync(replaced, token).ConfigureAwait(false);
                }
            }
            DateTime now = DateTime.UtcNow;
            SubscriptionStatusEnum status = MapGoogle(purchase.State, purchase.ExpiryUtc, now);
            return await UpsertAsync(StoreEnum.Google, purchase.PurchaseToken, userId, String.IsNullOrEmpty(purchase.ProductId) ? _Property.GooglePlayProductId : purchase.ProductId,
                status, purchase.StartUtc, purchase.ExpiryUtc, purchase.AutoRenewing, purchase.Environment, purchase.LatestOrderId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Apply a Stripe subscription to its subscription row and entitlement.
        /// </summary>
        /// <param name="stripe">Subscription from the Stripe API.</param>
        /// <param name="userId">Owning member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Subscription.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public Task<Subscription> ApplyStripeAsync(StripeSubscription stripe, string userId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(stripe);
            SubscriptionStatusEnum status = MapStripe(stripe.Status);
            bool renews = !stripe.CancelAtPeriodEnd && (status == SubscriptionStatusEnum.Active || status == SubscriptionStatusEnum.InGrace || status == SubscriptionStatusEnum.InBillingRetry);
            return UpsertAsync(StoreEnum.Stripe, stripe.Id, userId, stripe.PriceId ?? _Settings.Stripe.PriceId, status, stripe.CurrentPeriodStartUtc, stripe.CurrentPeriodEndUtc,
                renews, stripe.LiveMode ? StoreEnvironmentEnum.Production : StoreEnvironmentEnum.Sandbox, null, token);
        }

        /// <summary>
        /// Re-query one subscription from its store and apply the result (reconciliation).
        /// </summary>
        /// <param name="subscription">Subscription.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the store was queried; false when the store is not configured or returned nothing.</returns>
        /// <exception cref="StoreVerificationException">The store call failed.</exception>
        public async Task<bool> ReconcileAsync(Subscription subscription, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(subscription);
            Subscription applied;
            string raw;
            switch (subscription.Store)
            {
                case StoreEnum.Stripe:
                    if (!_Integrations.Stripe.IsConfigured) return false;
                    StripeSubscription stripe = await _Integrations.Stripe.GetSubscriptionAsync(subscription.StoreSubscriptionId, token).ConfigureAwait(false);
                    applied = await ApplyStripeAsync(stripe, subscription.UserId, token).ConfigureAwait(false);
                    raw = stripe.RawPayloadJson;
                    break;
                case StoreEnum.Apple:
                    if (!_Integrations.AppStore.IsConfigured) return false;
                    AppStoreTransaction? transaction = await _Integrations.AppStore.GetLatestTransactionAsync(subscription.StoreSubscriptionId, subscription.Environment, token).ConfigureAwait(false);
                    if (transaction == null) return false;
                    applied = await ApplyAppleAsync(transaction, subscription.UserId, null, null, token).ConfigureAwait(false);
                    raw = transaction.RawPayloadJson;
                    break;
                default:
                    if (!_Integrations.GooglePlay.IsConfigured) return false;
                    GooglePlaySubscription purchase = await _Integrations.GooglePlay.GetSubscriptionAsync(subscription.StoreSubscriptionId, token).ConfigureAwait(false);
                    purchase.PurchaseToken = subscription.StoreSubscriptionId;
                    applied = await ApplyGoogleAsync(purchase, subscription.UserId, token).ConfigureAwait(false);
                    raw = purchase.RawPayloadJson;
                    break;
            }
            await RecordEventAsync(applied, subscription.Store, SubscriptionEventSourceEnum.Reconcile, "RECONCILE", raw, token).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Record a verify or reconcile event.
        /// </summary>
        /// <param name="subscription">Subscription.</param>
        /// <param name="store">Store.</param>
        /// <param name="source">Source.</param>
        /// <param name="eventType">Event type.</param>
        /// <param name="rawPayloadJson">Raw payload.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task RecordEventAsync(Subscription? subscription, StoreEnum store, SubscriptionEventSourceEnum source, string eventType, string? rawPayloadJson, CancellationToken token = default)
        {
            string id = IdGenerator.GenerateSubscriptionEventId();
            DateTime now = DateTime.UtcNow;
            await _Database.SubscriptionEvents.TryCreateAsync(new SubscriptionEvent
            {
                Id = id,
                SubscriptionId = subscription?.Id,
                UserId = subscription?.UserId,
                Store = store,
                StoreEventId = source.ToString().ToLowerInvariant() + ":" + id,
                Source = source,
                EventType = eventType,
                ReceivedUtc = now,
                ProcessedUtc = now,
                Outcome = "Processed",
                RawPayloadJson = rawPayloadJson
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Map a Stripe subscription status.
        /// </summary>
        /// <param name="status">Stripe status.</param>
        /// <returns>Normalized status.</returns>
        public static SubscriptionStatusEnum MapStripe(string? status)
        {
            switch ((status ?? String.Empty).ToLowerInvariant())
            {
                case "active":
                case "trialing":
                    return SubscriptionStatusEnum.Active;
                case "past_due":
                    return SubscriptionStatusEnum.InGrace;
                case "unpaid":
                case "incomplete":
                    return SubscriptionStatusEnum.InBillingRetry;
                case "paused":
                    return SubscriptionStatusEnum.Paused;
                default:
                    return SubscriptionStatusEnum.Expired;
            }
        }

        /// <summary>
        /// Map a Google Play subscription state.
        /// </summary>
        /// <param name="state">State.</param>
        /// <param name="expiryUtc">Expiry of the current period.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>Normalized status.</returns>
        public static SubscriptionStatusEnum MapGoogle(GooglePlaySubscriptionStateEnum state, DateTime? expiryUtc, DateTime nowUtc)
        {
            switch (state)
            {
                case GooglePlaySubscriptionStateEnum.Active:
                    return SubscriptionStatusEnum.Active;
                case GooglePlaySubscriptionStateEnum.Canceled:
                    return expiryUtc != null && expiryUtc.Value > nowUtc ? SubscriptionStatusEnum.Active : SubscriptionStatusEnum.Expired;
                case GooglePlaySubscriptionStateEnum.InGracePeriod:
                    return SubscriptionStatusEnum.InGrace;
                case GooglePlaySubscriptionStateEnum.OnHold:
                case GooglePlaySubscriptionStateEnum.Pending:
                    return SubscriptionStatusEnum.InBillingRetry;
                case GooglePlaySubscriptionStateEnum.Paused:
                    return SubscriptionStatusEnum.Paused;
                default:
                    return SubscriptionStatusEnum.Expired;
            }
        }

        /// <summary>
        /// 503 error for a store that is not configured, naming its settings.
        /// </summary>
        /// <param name="store">Store.</param>
        /// <returns>Exception.</returns>
        public static ApiErrorException NotConfigured(StoreEnum store)
        {
            switch (store)
            {
                case StoreEnum.Apple:
                    return new ApiErrorException(503, "IntegrationNotConfigured", "App Store purchases are not configured. Set appStore.bundleId, appStore.issuerId, appStore.keyId, and appStore.privateKeyPem.");
                case StoreEnum.Google:
                    return new ApiErrorException(503, "IntegrationNotConfigured", "Google Play purchases are not configured. Set googlePlay.packageName and googlePlay.serviceAccountJson.");
                default:
                    return new ApiErrorException(503, "IntegrationNotConfigured", "Stripe is not configured. Set stripe.secretKey, stripe.webhookSecret, and stripe.priceId.");
            }
        }

        #endregion

        #region Private-Methods

        private async Task<Subscription> UpsertAsync(StoreEnum store, string storeId, string userId, string productId, SubscriptionStatusEnum status, DateTime? periodStart,
            DateTime? periodEnd, bool? autoRenew, StoreEnvironmentEnum environment, string? transactionId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(storeId)) throw new StoreVerificationException("The store returned no subscription identifier.");
            DateTime now = DateTime.UtcNow;
            Subscription? subscription = await _Database.Subscriptions.ReadByStoreIdAsync(store, storeId, token).ConfigureAwait(false);
            SubscriptionStatusEnum? previousStatus = subscription?.Status;
            bool previousAutoRenew = subscription?.AutoRenew ?? false;
            DateTime? previousEnd = subscription?.CurrentPeriodEndUtc;
            bool created = false;
            if (subscription == null)
            {
                subscription = new Subscription
                {
                    Id = IdGenerator.GenerateSubscriptionId(),
                    UserId = userId,
                    Store = store,
                    StoreSubscriptionId = storeId,
                    CreatedUtc = now,
                    AutoRenew = autoRenew ?? (status == SubscriptionStatusEnum.Active)
                };
                created = true;
            }
            subscription.ProductId = productId;
            subscription.Status = status;
            subscription.CurrentPeriodStartUtc = periodStart ?? subscription.CurrentPeriodStartUtc;
            subscription.CurrentPeriodEndUtc = periodEnd ?? subscription.CurrentPeriodEndUtc;
            if (autoRenew != null) subscription.AutoRenew = autoRenew.Value;
            if (status == SubscriptionStatusEnum.Expired || status == SubscriptionStatusEnum.Revoked || status == SubscriptionStatusEnum.Refunded) subscription.AutoRenew = false;
            subscription.Environment = environment;
            subscription.LatestTransactionId = transactionId ?? subscription.LatestTransactionId;
            subscription.UpdatedUtc = now;
            subscription.LastEventUtc = now;

            if (created)
            {
                try
                {
                    await _Database.Subscriptions.CreateAsync(subscription, token).ConfigureAwait(false);
                    if (status == SubscriptionStatusEnum.Active) TrellisKitTelemetry.Increment(TrellisKitTelemetry.Purchases, "store", store.ToString(), "outcome", "activated");
                }
                catch (DuplicateRecordException)
                {
                    Subscription concurrent = await _Database.Subscriptions.ReadByStoreIdAsync(store, storeId, token).ConfigureAwait(false)
                        ?? throw new StoreVerificationException("The subscription could not be stored.");
                    subscription.Id = concurrent.Id;
                    subscription.UserId = concurrent.UserId;
                    subscription.CreatedUtc = concurrent.CreatedUtc;
                    await _Database.Subscriptions.UpdateAsync(subscription, token).ConfigureAwait(false);
                }
            }
            else
            {
                await _Database.Subscriptions.UpdateAsync(subscription, token).ConfigureAwait(false);
            }
            await SyncEntitlementAsync(subscription, token).ConfigureAwait(false);
            await NotifyAsync(subscription, previousStatus, previousAutoRenew, previousEnd, token).ConfigureAwait(false);
            return subscription;
        }

        private async Task NotifyAsync(Subscription s, SubscriptionStatusEnum? before, bool autoRenewBefore, DateTime? endBefore, CancellationToken token)
        {
            // Billing lifecycle emails (docs/TRELLISKIT.md section 12.3), one per event: the idempotency key carries the
            // subscription, the transition, and the period, so a repeated store notification never sends twice.
            if (Email == null) return;
            try
            {
                User? user = await _Database.Users.ReadAsync(s.UserId, token).ConfigureAwait(false);
                if (user == null || user.IsDeleted) return;
                string store = s.Store == StoreEnum.Apple ? "the App Store" : s.Store == StoreEnum.Google ? "Google Play" : "the web";
                string end = s.CurrentPeriodEndUtc == null ? "the end of the period" : EmailService.FormatDate(s.CurrentPeriodEndUtc.Value);
                string period = (s.CurrentPeriodEndUtc ?? s.CreatedUtc).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string link = Email.Link("/app/membership");
                string admin = Email.Link("/admin/members/" + user.Id);
                bool active = s.Status == SubscriptionStatusEnum.Active;
                bool wasActive = before == SubscriptionStatusEnum.Active || before == SubscriptionStatusEnum.InGrace || before == SubscriptionStatusEnum.InBillingRetry;
                string key = "sub:" + s.Id + ":";
                if (active && !wasActive)
                {
                    await Email.SendToMemberAsync("membership_started", user, key + "started:" + period, token, ("plan", _Property.Property.Membership.ProductName), ("store", store), ("renewalDate", end), ("link", link)).ConfigureAwait(false);
                    await Email.NotifyStaffAsync("staff_new_subscription", key + "started:" + period, token, ("subjectName", user.DisplayName), ("store", store), ("link", admin)).ConfigureAwait(false);
                }
                else if (active && before == SubscriptionStatusEnum.Active && endBefore != null && s.CurrentPeriodEndUtc != null && s.CurrentPeriodEndUtc > endBefore)
                    await Email.SendToMemberAsync("renewal_succeeded", user, key + "renewed:" + period, token, ("store", store), ("renewalDate", end), ("link", link)).ConfigureAwait(false);
                if ((s.Status == SubscriptionStatusEnum.InBillingRetry || s.Status == SubscriptionStatusEnum.InGrace) && before != s.Status)
                {
                    string fix = s.Store == StoreEnum.Apple ? "Update your payment method in your Apple account settings." : s.Store == StoreEnum.Google ? "Update your payment method in Google Play." : "Update your card on the membership page.";
                    await Email.SendToMemberAsync("payment_failed", user, key + "failed:" + s.Status + ":" + period, token, ("store", store), ("fixInstructions", fix), ("link", link)).ConfigureAwait(false);
                    await Email.NotifyStaffAsync("staff_payment_failure", key + "failed:" + s.Status + ":" + period, token, ("subjectName", user.DisplayName), ("store", store), ("link", admin)).ConfigureAwait(false);
                }
                if (autoRenewBefore && !s.AutoRenew && (active || s.Status == SubscriptionStatusEnum.InGrace))
                {
                    await Email.SendToMemberAsync("subscription_canceled", user, key + "canceled:" + period, token, ("endDate", end), ("link", link)).ConfigureAwait(false);
                    await Email.NotifyStaffAsync("staff_cancellation", key + "canceled:" + period, token, ("subjectName", user.DisplayName), ("store", store), ("endDate", end), ("link", admin)).ConfigureAwait(false);
                }
                if (s.Status == SubscriptionStatusEnum.Expired && before != null && before != SubscriptionStatusEnum.Expired)
                    await Email.SendToMemberAsync("membership_expired", user, key + "expired:" + period, token, ("link", Email.Link("/join"))).ConfigureAwait(false);
                if ((s.Status == SubscriptionStatusEnum.Refunded || s.Status == SubscriptionStatusEnum.Revoked) && before != s.Status)
                {
                    await Email.SendToMemberAsync("refund_revoked", user, key + "refund", token, ("store", store)).ConfigureAwait(false);
                    await Email.NotifyStaffAsync("staff_refund", key + "refund", token, ("subjectName", user.DisplayName), ("store", store), ("link", admin)).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logger.Warn("[PurchaseService] membership email failed: " + e.Message);
            }
        }

        // Keeps a subscription's entitlement in step with its status: entitled while Active or InGrace through the
        // period end; revoked when refunded, revoked, or ended early.
        private async Task SyncEntitlementAsync(Subscription subscription, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            Entitlement? entitlement = await _Database.Entitlements.ReadBySourceAsync(MembershipSourceEnum.Subscription, subscription.Id, token).ConfigureAwait(false);
            bool entitled = subscription.Status == SubscriptionStatusEnum.Active || subscription.Status == SubscriptionStatusEnum.InGrace;
            DateTime starts = entitlement?.StartsUtc ?? subscription.CurrentPeriodStartUtc ?? subscription.CreatedUtc;
            if (starts > now) starts = now;
            DateTime? ends = subscription.CurrentPeriodEndUtc;

            DateTime? revoked = null;
            string? reason = null;
            bool naturallyEnded = ends != null && ends.Value <= now;
            if (!entitled && (!naturallyEnded || subscription.Status == SubscriptionStatusEnum.Refunded || subscription.Status == SubscriptionStatusEnum.Revoked))
            {
                revoked = entitlement?.RevokedUtc ?? now;
                reason = subscription.Status.ToString();
            }

            if (entitlement == null)
            {
                if (!entitled && revoked == null && naturallyEnded && ends == null) return;
                await _Database.Entitlements.CreateAsync(new Entitlement
                {
                    Id = IdGenerator.GenerateEntitlementId(),
                    UserId = subscription.UserId,
                    Source = MembershipSourceEnum.Subscription,
                    SourceId = subscription.Id,
                    StartsUtc = starts,
                    EndsUtc = ends ?? now,
                    RevokedUtc = revoked,
                    RevokeReason = reason,
                    CreatedUtc = now,
                    UpdatedUtc = now
                }, token).ConfigureAwait(false);
            }
            else
            {
                entitlement.StartsUtc = starts;
                entitlement.EndsUtc = ends ?? entitlement.EndsUtc;
                entitlement.RevokedUtc = revoked;
                entitlement.RevokeReason = reason;
                entitlement.UpdatedUtc = now;
                await _Database.Entitlements.UpdateAsync(entitlement, token).ConfigureAwait(false);
            }
            _Tokens.InvalidateUser(subscription.UserId);
        }

        private void RequireStripe()
        {
            if (!_Integrations.Stripe.IsConfigured || String.IsNullOrWhiteSpace(_Settings.Stripe.PriceId)) throw NotConfigured(StoreEnum.Stripe);
        }

        private async Task<User> RequireUserAsync(string userId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(userId)) throw ApiErrorException.Forbidden("This route needs a member session.");
            User? user = await _Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) throw new ApiErrorException(401, "Unauthorized", "Sign in again.");
            return user;
        }

        private static ApiErrorException Verification(string store, string outcome, ApiErrorException error)
        {
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.Verifications, "store", store, "outcome", outcome);
            return error;
        }

        private static ApiErrorException BelongsToAnother()
        {
            return new ApiErrorException(409, "TransactionBelongsToAnotherAccount", "This purchase belongs to another account. Sign in with the account that bought it.");
        }

        private static ApiErrorException StoreError(string store, Exception e)
        {
            return new ApiErrorException(502, "StoreError", store + " could not complete the request. Try again shortly.", null, null);
        }

        #endregion
    }
}
