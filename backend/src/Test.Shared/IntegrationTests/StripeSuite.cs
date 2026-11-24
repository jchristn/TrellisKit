namespace Test.Shared.IntegrationTests
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Stripe REST requests against a fake API and webhook signature verification.
    /// </summary>
    public static class StripeSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "IntegrationStripe";

        #endregion

        #region Private-Members

        private const string ApiUrl = "https://stripe.test";
        private const string SecretKey = "sk_test_51FakeSecretKeyForTouchstoneTests";
        private const string WebhookSecret = "whsec_test_webhook_secret_value";
        private const string PriceId = "price_annual_test";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Stripe client and webhooks", new[]
            {
                Case("Webhook.Valid", "A correctly signed checkout event is parsed", ct =>
                {
                    StripeClient client = new StripeClient(Settings(), new HttpClient());
                    DateTime now = DateTime.UtcNow;
                    string payload = CheckoutEvent();
                    StripeEvent e = client.ParseWebhook(payload, Header(payload, now, WebhookSecret), now);
                    Check.Equal("evt_1", e.Id, "Id");
                    Check.Equal("checkout.session.completed", e.Type, "Type");
                    Check.Equal("sub_123", e.SubscriptionId, "Subscription");
                    Check.Equal("cus_123", e.CustomerId, "Customer");
                    Check.Equal("usr_abc", e.ClientReferenceId, "Client reference");
                    Check.True(!e.LiveMode, "Test mode");
                    Check.Equal(new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc), e.CreatedUtc, "Created");
                    Check.Equal(payload, e.RawPayloadJson, "Raw payload");
                    Check.True(e.SignupCodeId == null, "No discount code");

                    string discounted = payload.Replace("\"client_reference_id\":\"usr_abc\"", "\"client_reference_id\":\"usr_abc\",\"metadata\":{\"userId\":\"usr_abc\",\"signupCodeId\":\"sc_1\"}", StringComparison.Ordinal);
                    StripeEvent withCode = client.ParseWebhook(discounted, Header(discounted, now, WebhookSecret), now);
                    Check.Equal("sc_1", withCode.SignupCodeId, "Discount code from session metadata");
                    return Task.CompletedTask;
                }),
                Case("Webhook.Tampered", "A payload changed after signing is rejected", ct =>
                {
                    StripeClient client = new StripeClient(Settings(), new HttpClient());
                    DateTime now = DateTime.UtcNow;
                    string payload = CheckoutEvent();
                    string header = Header(payload, now, WebhookSecret);
                    Check.Throws<WebhookSignatureException>(() => client.ParseWebhook(payload.Replace("usr_abc", "usr_evil"), header, now), "Tampered");
                    Check.Throws<WebhookSignatureException>(() => client.ParseWebhook(payload, Header(payload, now, "whsec_other"), now), "Other secret");
                    Check.Throws<WebhookSignatureException>(() => client.ParseWebhook(payload, null, now), "Missing header");
                    Check.Throws<WebhookSignatureException>(() => client.ParseWebhook(payload, "v1=abcdef", now), "No timestamp");
                    Check.Throws<WebhookSignatureException>(() => client.ParseWebhook(payload, "t=1700000000", now), "No v1");
                    return Task.CompletedTask;
                }),
                Case("Webhook.Stale", "Timestamps more than 300 seconds away are rejected", ct =>
                {
                    StripeClient client = new StripeClient(Settings(), new HttpClient());
                    DateTime signed = DateTime.UtcNow.AddSeconds(-301);
                    string payload = CheckoutEvent();
                    Check.Throws<WebhookSignatureException>(() => client.ParseWebhook(payload, Header(payload, signed, WebhookSecret), DateTime.UtcNow), "Stale");
                    DateTime recent = DateTime.UtcNow.AddSeconds(-290);
                    client.ParseWebhook(payload, Header(payload, recent, WebhookSecret), DateTime.UtcNow);
                    return Task.CompletedTask;
                }),
                Case("Webhook.MultipleSignatures", "Any matching v1 signature is accepted (secret rotation)", ct =>
                {
                    StripeClient client = new StripeClient(Settings(), new HttpClient());
                    DateTime now = DateTime.UtcNow;
                    string payload = CheckoutEvent();
                    long t = new DateTimeOffset(now).ToUnixTimeSeconds();
                    string header = "t=" + t.ToString(CultureInfo.InvariantCulture)
                        + ",v1=" + Sign(payload, t, "whsec_old_secret")
                        + ",v1=" + Sign(payload, t, WebhookSecret)
                        + ",v0=deadbeef";
                    StripeEvent e = client.ParseWebhook(payload, header, now);
                    Check.Equal("evt_1", e.Id, "Parsed");
                    return Task.CompletedTask;
                }),
                Case("Webhook.EventShapes", "Subscription and invoice events fill subscription and customer ids", ct =>
                {
                    StripeClient client = new StripeClient(Settings(), new HttpClient());
                    DateTime now = DateTime.UtcNow;
                    string subscription = "{\"id\":\"evt_2\",\"type\":\"customer.subscription.updated\",\"created\":1700000000,\"livemode\":true,\"data\":{\"object\":{\"id\":\"sub_9\",\"object\":\"subscription\",\"customer\":\"cus_9\"}}}";
                    StripeEvent s = client.ParseWebhook(subscription, Header(subscription, now, WebhookSecret), now);
                    Check.Equal("sub_9", s.SubscriptionId, "Subscription id");
                    Check.Equal("cus_9", s.CustomerId, "Customer id");
                    Check.True(s.LiveMode, "Live mode");

                    string invoice = "{\"id\":\"evt_3\",\"type\":\"invoice.paid\",\"created\":1700000000,\"livemode\":false,\"data\":{\"object\":{\"id\":\"in_1\",\"object\":\"invoice\",\"customer\":\"cus_7\",\"parent\":{\"type\":\"subscription_details\",\"subscription_details\":{\"subscription\":\"sub_7\"}}}}}";
                    StripeEvent i = client.ParseWebhook(invoice, Header(invoice, now, WebhookSecret), now);
                    Check.Equal("sub_7", i.SubscriptionId, "Invoice subscription (parent)");
                    Check.Equal("cus_7", i.CustomerId, "Invoice customer");

                    string legacy = "{\"id\":\"evt_4\",\"type\":\"invoice.payment_failed\",\"created\":1700000000,\"data\":{\"object\":{\"id\":\"in_2\",\"customer\":\"cus_8\",\"subscription\":\"sub_8\"}}}";
                    StripeEvent l = client.ParseWebhook(legacy, Header(legacy, now, WebhookSecret), now);
                    Check.Equal("sub_8", l.SubscriptionId, "Invoice subscription (legacy)");
                    return Task.CompletedTask;
                }),
                Case("Webhook.NotConfigured", "Without a webhook secret webhooks are refused", ct =>
                {
                    StripeSettings settings = Settings();
                    settings.WebhookSecret = String.Empty;
                    StripeClient client = new StripeClient(settings, new HttpClient());
                    Check.Throws<IntegrationNotConfiguredException>(() => client.ParseWebhook("{}", "t=1,v1=00", DateTime.UtcNow), "No secret");
                    settings.SecretKey = String.Empty;
                    Check.True(!client.IsConfigured, "Not configured without a secret key");
                    return Task.CompletedTask;
                }),
                Case("Checkout.Form", "Checkout posts the expected form fields, headers, and idempotency key", async ct =>
                {
                    using (TestHttp http = new TestHttp())
                    {
                        http.Handler.MapJson("POST", ApiUrl + "/v1/checkout/sessions", 200, "{\"id\":\"cs_test_1\",\"object\":\"checkout.session\",\"url\":\"https://checkout.stripe.com/c/pay/cs_test_1\"}");
                        StripeClient client = new StripeClient(Settings(), http.Client);
                        StripeCheckoutSession session = await client.CreateCheckoutSessionAsync(new StripeCheckoutRequest
                        {
                            UserId = "usr_abc",
                            Email = "member@example.com",
                            PriceId = PriceId,
                            SuccessUrl = "https://example.test/app/membership?checkout=success",
                            CancelUrl = "https://example.test/app/membership?checkout=cancelled"
                        }, ct).ConfigureAwait(false);
                        Check.Equal("cs_test_1", session.Id, "Session id");
                        Check.Equal("https://checkout.stripe.com/c/pay/cs_test_1", session.Url, "Session URL");

                        RecordedRequest request = http.Handler.Requests[0];
                        Check.Equal("Bearer " + SecretKey, request.Header("Authorization"), "Authorization");
                        Check.True(!String.IsNullOrEmpty(request.Header("Stripe-Version")), "Stripe-Version pinned");
                        Check.True(!String.IsNullOrEmpty(request.Header("Idempotency-Key")), "Idempotency key");
                        Check.True((request.Header("Content-Type") ?? String.Empty).StartsWith("application/x-www-form-urlencoded", StringComparison.Ordinal), "Form encoded");
                        Check.True(request.Body.Contains("line_items%5B0%5D%5Bprice%5D=" + PriceId, StringComparison.Ordinal), "Brackets are percent-encoded");
                        Dictionary<string, string> form = request.Form();
                        Check.Equal("subscription", form["mode"], "mode");
                        Check.Equal(PriceId, form["line_items[0][price]"], "price");
                        Check.Equal("1", form["line_items[0][quantity]"], "quantity");
                        Check.Equal("usr_abc", form["client_reference_id"], "client_reference_id");
                        Check.Equal("usr_abc", form["subscription_data[metadata][userId]"], "subscription metadata");
                        Check.Equal("member@example.com", form["customer_email"], "customer_email");
                        Check.True(!form.ContainsKey("customer"), "No customer");
                        Check.Equal("https://example.test/app/membership?checkout=success", form["success_url"], "success_url");
                        Check.Equal("https://example.test/app/membership?checkout=cancelled", form["cancel_url"], "cancel_url");
                        Check.Equal("false", form["allow_promotion_codes"], "allow_promotion_codes");
                        Check.Equal("true", form["automatic_tax[enabled]"], "automatic_tax");

                        await client.CreateCheckoutSessionAsync(new StripeCheckoutRequest
                        {
                            UserId = "usr_abc",
                            CustomerId = "cus_123",
                            Email = "member@example.com",
                            PriceId = PriceId,
                            SuccessUrl = "https://example.test/s",
                            CancelUrl = "https://example.test/c"
                        }, ct).ConfigureAwait(false);
                        RecordedRequest second = http.Handler.Requests[1];
                        Dictionary<string, string> secondForm = second.Form();
                        Check.Equal("cus_123", secondForm["customer"], "customer");
                        Check.True(!secondForm.ContainsKey("customer_email"), "No customer_email with a customer");
                        Check.True(second.Header("Idempotency-Key") != request.Header("Idempotency-Key"), "Fresh idempotency key per request");
                    }
                }),
                Case("Checkout.Discount", "A discounted checkout sends the coupon and code metadata and omits allow_promotion_codes", async ct =>
                {
                    using (TestHttp http = new TestHttp())
                    {
                        http.Handler.MapJson("POST", ApiUrl + "/v1/checkout/sessions", 200, "{\"id\":\"cs_test_2\",\"object\":\"checkout.session\",\"url\":\"https://checkout.stripe.com/c/pay/cs_test_2\"}");
                        StripeClient client = new StripeClient(Settings(), http.Client);
                        await client.CreateCheckoutSessionAsync(new StripeCheckoutRequest
                        {
                            UserId = "usr_abc",
                            Email = "member@example.com",
                            PriceId = PriceId,
                            SuccessUrl = "https://example.test/s",
                            CancelUrl = "https://example.test/c",
                            CouponId = "coupon_1",
                            SignupCodeId = "sc_1"
                        }, ct).ConfigureAwait(false);
                        Dictionary<string, string> form = http.Handler.Requests[0].Form();
                        Check.Equal("coupon_1", form["discounts[0][coupon]"], "coupon");
                        Check.True(!form.ContainsKey("allow_promotion_codes"), "No allow_promotion_codes with discounts");
                        Check.Equal("sc_1", form["metadata[signupCodeId]"], "session metadata");
                        Check.Equal("sc_1", form["subscription_data[metadata][signupCodeId]"], "subscription metadata");
                    }
                }),
                Case("Coupon.Create", "Coupons post percent or amount off with the duration, name, and code metadata", async ct =>
                {
                    using (TestHttp http = new TestHttp())
                    {
                        http.Handler.MapJson("POST", ApiUrl + "/v1/coupons", 200, "{\"id\":\"coupon_abc\",\"object\":\"coupon\"}");
                        StripeClient client = new StripeClient(Settings(), http.Client);
                        string id = await client.CreateCouponAsync(new StripeCouponRequest { Name = "Seminar 40", PercentOff = 40, SignupCodeId = "sc_1" }, ct).ConfigureAwait(false);
                        Check.Equal("coupon_abc", id, "Coupon id");
                        Dictionary<string, string> form = http.Handler.Requests[0].Form();
                        Check.Equal("40", form["percent_off"], "percent_off");
                        Check.Equal("once", form["duration"], "duration once");
                        Check.Equal("Seminar 40", form["name"], "name");
                        Check.Equal("sc_1", form["metadata[signupCodeId]"], "metadata");
                        Check.True(!form.ContainsKey("amount_off"), "No amount_off");

                        await client.CreateCouponAsync(new StripeCouponRequest { Name = "Ten off", AmountOffCents = 1000, Forever = true, SignupCodeId = "sc_2" }, ct).ConfigureAwait(false);
                        Dictionary<string, string> second = http.Handler.Requests[1].Form();
                        Check.Equal("1000", second["amount_off"], "amount_off");
                        Check.Equal("usd", second["currency"], "currency");
                        Check.Equal("forever", second["duration"], "duration forever");
                        await Check.ThrowsAsync<ArgumentException>(() => client.CreateCouponAsync(new StripeCouponRequest { Name = "Both", PercentOff = 10, AmountOffCents = 100 }, ct), "Both set").ConfigureAwait(false);
                    }
                }),
                Case("Errors.Mapped", "Stripe errors carry the error type and message, never the key", async ct =>
                {
                    using (TestHttp http = new TestHttp())
                    {
                        http.Handler.MapJson("POST", ApiUrl + "/v1/billing_portal/sessions", 401, "{\"error\":{\"type\":\"invalid_request_error\",\"message\":\"Invalid API Key provided: " + SecretKey + "\"}}");
                        http.Handler.MapJson("GET", ApiUrl + "/v1/subscriptions/", 404, "{\"error\":{\"type\":\"invalid_request_error\",\"code\":\"resource_missing\",\"message\":\"No such subscription: 'sub_missing'\",\"param\":\"id\"}}");
                        StripeClient client = new StripeClient(Settings(), http.Client);
                        StoreVerificationException e = await Check.ThrowsAsync<StoreVerificationException>(() => client.CreatePortalSessionAsync("cus_1", "https://example.test/app", ct), "Portal").ConfigureAwait(false);
                        Check.True(e.Message.Contains("invalid_request_error", StringComparison.Ordinal), "Type included");
                        Check.True(e.Message.Contains("Invalid API Key provided", StringComparison.Ordinal), "Message included");
                        Check.True(!e.Message.Contains(SecretKey, StringComparison.Ordinal) && !e.Message.Contains("sk_test_", StringComparison.Ordinal), "Key redacted");

                        StoreVerificationException missing = await Check.ThrowsAsync<StoreVerificationException>(() => client.GetSubscriptionAsync("sub_missing", ct), "Get").ConfigureAwait(false);
                        Check.True(missing.Message.Contains("resource_missing", StringComparison.Ordinal) && missing.Message.Contains("No such subscription", StringComparison.Ordinal), "Code and message");
                    }
                }),
                Case("Subscription.Map", "Subscriptions map period from items, metadata, and cancellation", async ct =>
                {
                    using (TestHttp http = new TestHttp())
                    {
                        string body = "{\"id\":\"sub_1\",\"object\":\"subscription\",\"customer\":\"cus_1\",\"status\":\"active\",\"cancel_at_period_end\":true,\"livemode\":false,"
                            + "\"metadata\":{\"userId\":\"usr_abc\"},\"items\":{\"object\":\"list\",\"data\":[{\"id\":\"si_1\",\"current_period_start\":1700000000,\"current_period_end\":1731536000,\"price\":{\"id\":\"" + PriceId + "\"}}]}}";
                        http.Handler.MapJson("POST", ApiUrl + "/v1/subscriptions/sub_1", 200, body);
                        http.Handler.MapJson("POST", ApiUrl + "/v1/billing_portal/sessions", 200, "{\"id\":\"bps_1\",\"url\":\"https://billing.stripe.com/p/session/test\"}");
                        StripeClient client = new StripeClient(Settings(), http.Client);
                        StripeSubscription subscription = await client.CancelAtPeriodEndAsync("sub_1", ct).ConfigureAwait(false);
                        Check.Equal("sub_1", subscription.Id, "Id");
                        Check.Equal("cus_1", subscription.CustomerId, "Customer");
                        Check.Equal("active", subscription.Status, "Status");
                        Check.Equal(PriceId, subscription.PriceId, "Price");
                        Check.True(subscription.CancelAtPeriodEnd, "Cancel at period end");
                        Check.Equal("usr_abc", subscription.UserId, "User id");
                        Check.Equal((DateTime?)DateTimeOffset.FromUnixTimeSeconds(1731536000).UtcDateTime, subscription.CurrentPeriodEndUtc, "Period end from items");
                        Check.Equal("true", http.Handler.Requests[0].Form()["cancel_at_period_end"], "cancel_at_period_end posted");

                        string url = await client.CreatePortalSessionAsync("cus_1", "https://example.test/app/membership", ct).ConfigureAwait(false);
                        Check.Equal("https://billing.stripe.com/p/session/test", url, "Portal URL");
                        Dictionary<string, string> portal = http.Handler.Requests[1].Form();
                        Check.Equal("cus_1", portal["customer"], "Portal customer");
                        Check.Equal("https://example.test/app/membership", portal["return_url"], "Portal return URL");
                    }
                })
            });
        }

        #endregion

        #region Private-Methods

        private static StripeSettings Settings()
        {
            return new StripeSettings { SecretKey = SecretKey, WebhookSecret = WebhookSecret, PriceId = PriceId, ApiBaseUrl = ApiUrl };
        }

        private static string CheckoutEvent()
        {
            return "{\"id\":\"evt_1\",\"object\":\"event\",\"type\":\"checkout.session.completed\",\"created\":1700000000,\"livemode\":false,"
                + "\"data\":{\"object\":{\"id\":\"cs_1\",\"object\":\"checkout.session\",\"mode\":\"subscription\",\"customer\":\"cus_123\",\"subscription\":\"sub_123\",\"client_reference_id\":\"usr_abc\"}}}";
        }

        private static string Header(string payload, DateTime signedUtc, string secret)
        {
            long t = new DateTimeOffset(signedUtc).ToUnixTimeSeconds();
            return "t=" + t.ToString(CultureInfo.InvariantCulture) + ",v1=" + Sign(payload, t, secret);
        }

        private static string Sign(string payload, long t, string secret)
        {
            using (HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            {
                return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(t.ToString(CultureInfo.InvariantCulture) + "." + payload))).ToLowerInvariant();
            }
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(Id, caseId, name, body, new[] { "fast", "unit", "integrations" });
        }

        #endregion
    }
}
