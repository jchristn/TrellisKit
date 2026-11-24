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
    /// Stripe client backed by in-memory subscriptions. Webhooks are JSON { id, type, subscriptionId, customerId,
    /// clientReferenceId, signupCodeId } and verify when the signature header is "valid".
    /// </summary>
    public sealed class FakeStripeClient : IStripeClient
    {
        private readonly ConcurrentDictionary<string, StripeSubscription> _Subscriptions = new ConcurrentDictionary<string, StripeSubscription>(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _Cancelled = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<StripeCouponRequest> _Coupons = new ConcurrentQueue<StripeCouponRequest>();

        /// <summary>
        /// Coupons created, in order.
        /// </summary>
        public IReadOnlyList<StripeCouponRequest> Coupons
        {
            get { return _Coupons.ToList(); }
        }

        /// <summary>
        /// Whether Stripe reports configured. Default true.
        /// </summary>
        public bool Configured { get; set; } = true;

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return Configured; }
        }

        /// <summary>
        /// The last checkout request.
        /// </summary>
        public StripeCheckoutRequest? LastCheckout { get; private set; } = null;

        /// <summary>
        /// Subscriptions cancelled at period end.
        /// </summary>
        public IReadOnlyList<string> Cancelled
        {
            get { return _Cancelled.ToList(); }
        }

        /// <summary>
        /// Add or replace a subscription.
        /// </summary>
        /// <param name="subscription">Subscription.</param>
        public void Put(StripeSubscription subscription)
        {
            _Subscriptions[subscription.Id] = subscription;
        }

        /// <inheritdoc />
        public Task<StripeCheckoutSession> CreateCheckoutSessionAsync(StripeCheckoutRequest request, CancellationToken token = default)
        {
            LastCheckout = request;
            return Task.FromResult(new StripeCheckoutSession { Id = "cs_test_1", Url = "https://checkout.stripe.com/c/pay/cs_test_1" });
        }

        /// <inheritdoc />
        public Task<string> CreateCouponAsync(StripeCouponRequest request, CancellationToken token = default)
        {
            _Coupons.Enqueue(request);
            return Task.FromResult("coupon_test_" + _Coupons.Count);
        }

        /// <inheritdoc />
        public Task<string> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken token = default)
        {
            return Task.FromResult("https://billing.stripe.com/p/session/test_" + customerId);
        }

        /// <inheritdoc />
        public Task<StripeSubscription> GetSubscriptionAsync(string subscriptionId, CancellationToken token = default)
        {
            if (_Subscriptions.TryGetValue(subscriptionId, out StripeSubscription? subscription)) return Task.FromResult(subscription);
            throw new StoreVerificationException("No such subscription.");
        }

        /// <inheritdoc />
        public Task<StripeSubscription> CancelAtPeriodEndAsync(string subscriptionId, CancellationToken token = default)
        {
            _Cancelled.Enqueue(subscriptionId);
            if (_Subscriptions.TryGetValue(subscriptionId, out StripeSubscription? subscription))
            {
                subscription.CancelAtPeriodEnd = true;
                return Task.FromResult(subscription);
            }
            throw new StoreVerificationException("No such subscription.");
        }

        /// <inheritdoc />
        public StripeEvent ParseWebhook(string payload, string? signatureHeader, DateTime nowUtc)
        {
            if (signatureHeader != "valid") throw new WebhookSignatureException("Bad test signature.");
            using (JsonDocument doc = JsonDocument.Parse(payload))
            {
                JsonElement root = doc.RootElement;
                return new StripeEvent
                {
                    Id = Read(root, "id") ?? String.Empty,
                    Type = Read(root, "type") ?? String.Empty,
                    SubscriptionId = Read(root, "subscriptionId"),
                    CustomerId = Read(root, "customerId"),
                    ClientReferenceId = Read(root, "clientReferenceId"),
                    SignupCodeId = Read(root, "signupCodeId"),
                    CreatedUtc = nowUtc,
                    RawPayloadJson = payload
                };
            }
        }

        private static string? Read(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
    }
}
