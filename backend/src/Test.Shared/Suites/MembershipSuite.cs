namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Phase 1 membership over HTTP: the membership summary, the paywall, Stripe checkout, portal, and webhooks, App
    /// Store and Google Play verification and notifications, idempotency, and nightly reconciliation.
    /// </summary>
    public static class MembershipSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Membership";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Membership and purchases (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Membership.Shape", "GET /membership returns the offer, store identifiers, and history; comps make the member active", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    JsonElement before = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!before.GetProperty("membership").GetProperty("isActive").GetBoolean(), "Inactive");
                    Check.Equal(f.Property.Membership.PriceUsdCents / 100m, before.GetProperty("webOffer").GetProperty("priceUsd").GetDecimal(), "Price");
                    Check.Equal("Year", Api.Str(before.GetProperty("webOffer"), "interval"), "Interval");
                    Check.Equal(f.Property.Membership.StoreProductId, Api.Str(before, "storeProductId"), "Store product");
                    Check.True(Guid.TryParse(Api.Str(before, "storeAccountToken"), out Guid _), "Store account token is a UUID");
                    JsonElement again = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(Api.Str(before, "storeAccountToken"), Api.Str(again, "storeAccountToken"), "Stable token");
                    await Api.GrantCompAsync(f, member.Id, ct).ConfigureAwait(false);
                    JsonElement after = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement summary = after.GetProperty("membership");
                    Check.True(summary.GetProperty("isActive").GetBoolean(), "Active");
                    Check.Equal("AdminComp", Api.Str(summary, "source"), "Source");
                    Check.True(summary.GetProperty("expiresUtc").ValueKind == JsonValueKind.Null, "Lifetime");
                    Check.Equal("Active", Api.Str(after.GetProperty("history").GetProperty("objects")[0], "status"), "History");
                }),
                Case("Membership.LatestExpiryWins", "Overlapping comps: the latest end wins", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    DateTime end = DateTime.UtcNow.AddYears(1);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + member.Id + "/comp", HttpFixture.AdminToken, new { grantType = "DurationMonths", months = 3, reason = "a" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + member.Id + "/comp", HttpFixture.AdminToken, new { grantType = "FixedEndDate", endUtc = end, reason = "b" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement me = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    DateTime expires = me.GetProperty("membership").GetProperty("expiresUtc").GetDateTime().ToUniversalTime();
                    Check.True(Math.Abs((expires - end).TotalSeconds) < 2, "Expires at the later end");
                }),
                Case("Paywall.SystemAdminBypass", "SystemAdmin members pass the membership check", async (f, ct) =>
                {
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + admin.Id, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),
                Case("Stripe.CheckoutPortalWebhooks", "Checkout, webhook activation, idempotent redelivery, the portal, AlreadyMember, and cancellation", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/membership/stripe/portal", member.AccessToken, null, HttpStatusCode.NotFound, "NoStripeCustomer", ct).ConfigureAwait(false);
                    JsonElement checkout = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/membership/stripe/checkout", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Api.Str(checkout, "url").StartsWith("https://checkout.stripe.com/", StringComparison.Ordinal), "Checkout URL");
                    Check.Equal(f.Services.Property.PublicBaseUrl + "/app/membership?checkout=success", f.Fakes.Stripe.LastCheckout!.SuccessUrl, "Success URL");
                    Check.Equal(f.Services.Property.PublicBaseUrl + "/app/membership?checkout=cancelled", f.Fakes.Stripe.LastCheckout.CancelUrl, "Cancel URL");
                    Check.Equal(member.Id, f.Fakes.Stripe.LastCheckout.UserId, "Member id passed");

                    f.Fakes.Stripe.Put(new StripeSubscription { Id = "sub_1", CustomerId = "cus_1", Status = "active", PriceId = "price_test_annual", CurrentPeriodStartUtc = DateTime.UtcNow, CurrentPeriodEndUtc = DateTime.UtcNow.AddYears(1) });
                    string payload = "{\"id\":\"evt_1\",\"type\":\"checkout.session.completed\",\"subscriptionId\":\"sub_1\",\"customerId\":\"cus_1\",\"clientReferenceId\":\"" + member.Id + "\"}";
                    await StripeHookAsync(f, payload, "bad", HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    JsonElement processed = await StripeHookAsync(f, payload, "valid", HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Processed.", Api.Str(processed, "message"), "Processed");
                    JsonElement duplicate = await StripeHookAsync(f, payload, "valid", HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Duplicate.", Api.Str(duplicate, "message"), "Redelivery is idempotent");

                    JsonElement membership = (await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("membership");
                    Check.True(membership.GetProperty("isActive").GetBoolean(), "Active after webhook");
                    Check.Equal("Stripe", Api.Str(membership, "store"), "Store");
                    Check.True(membership.GetProperty("autoRenew").GetBoolean(), "Auto-renew");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + member.Id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/membership/stripe/checkout", member.AccessToken, null, HttpStatusCode.Conflict, "AlreadyMember", ct).ConfigureAwait(false);
                    JsonElement portal = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/membership/stripe/portal", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Api.Str(portal, "url").Contains("cus_1", StringComparison.Ordinal), "Portal for the customer");

                    f.Fakes.Stripe.Put(new StripeSubscription { Id = "sub_1", CustomerId = "cus_1", Status = "canceled", CurrentPeriodStartUtc = DateTime.UtcNow.AddYears(-1), CurrentPeriodEndUtc = DateTime.UtcNow.AddMinutes(-1) });
                    await StripeHookAsync(f, "{\"id\":\"evt_2\",\"type\":\"customer.subscription.deleted\",\"subscriptionId\":\"sub_1\",\"customerId\":\"cus_1\"}", "valid", HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement ended = (await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("membership");
                    Check.True(!ended.GetProperty("isActive").GetBoolean(), "Inactive after cancellation");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/users/" + member.Id, member.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);

                    JsonElement list = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/subscriptions?store=Stripe", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement item = list.GetProperty("objects")[0];
                    Check.Equal("Expired", Api.Str(item, "status"), "Status");
                    Check.Equal(member.Email, Api.Str(item, "memberEmail"), "Member email");
                    JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/subscriptions/" + Api.Str(item, "id"), HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("sub_1", Api.Str(detail, "storeSubscriptionId"), "Store id");
                    Check.Equal(2, detail.GetProperty("events").GetProperty("objects").GetArrayLength(), "Two webhook events (duplicate not stored twice)");
                    Check.True(Api.Str(detail.GetProperty("events").GetProperty("objects")[0], "rawPayload").Contains("evt_", StringComparison.Ordinal), "Raw payload kept");
                }),
                Case("Stripe.DiscountCodes", "Discount sign-up codes: admin create, validate, redeem refused, checkout coupon (created once), webhook redemption, revoke", async (f, ct) =>
                {
                    string suffix = Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes", HttpFixture.AdminToken, new { name = "Bad", grantType = "Discount", discountDuration = "Once" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes", HttpFixture.AdminToken, new { name = "Both", grantType = "Discount", discountPercent = 10, discountAmountCents = 500, discountDuration = "Once" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes", HttpFixture.AdminToken, new { name = "Pct", grantType = "Discount", discountPercent = 101, discountDuration = "Once" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + (await Api.RegisterAsync(f, ct).ConfigureAwait(false)).Id + "/comp", HttpFixture.AdminToken, new { grantType = "Discount", reason = "x" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);

                    JsonElement created = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes", HttpFixture.AdminToken,
                        new { code = "SAVE40-" + suffix, name = "Seminar 40", grantType = "Discount", discountPercent = 40, discountDuration = "Once" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    string id = Api.Str(created, "id");
                    string code = Api.Str(created, "code");
                    Check.Equal("Discount", Api.Str(created, "grantType"), "Grant type");
                    Check.Equal(40, created.GetProperty("discountPercent").GetInt32(), "Percent");
                    Check.Equal("Once", Api.Str(created, "discountDuration"), "Duration");
                    Check.Equal("40% off the first year", Api.Str(created, "grantSummary"), "Summary");
                    Check.True(!created.TryGetProperty("stripeCouponId", out JsonElement noCoupon) || noCoupon.ValueKind == JsonValueKind.Null, "No coupon yet");

                    JsonElement amount = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes", HttpFixture.AdminToken,
                        new { name = "Ten off", grantType = "Discount", discountAmountCents = 1000, discountDuration = "Forever" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    Check.Equal("$10.00 off every renewal", Api.Str(amount, "grantSummary"), "Amount summary");

                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    JsonElement valid = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/validate", member.AccessToken, new { code = code.ToLowerInvariant() }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(valid.GetProperty("valid").GetBoolean(), "Valid");
                    Check.Equal("Discount", Api.Str(valid, "kind"), "Kind");
                    Check.Equal("40% off the first year", Api.Str(valid, "discountSummary"), "Discount summary");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", member.AccessToken, new { code }, HttpStatusCode.BadRequest, "DiscountCodeAtCheckout", ct).ConfigureAwait(false);

                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/membership/stripe/checkout", member.AccessToken, new { code = "NOPE-" + suffix }, HttpStatusCode.BadRequest, "InvalidCode", ct).ConfigureAwait(false);
                    JsonElement free = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes", HttpFixture.AdminToken, new { name = "Free", grantType = "Lifetime" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/membership/stripe/checkout", member.AccessToken, new { code = Api.Str(free, "code") }, HttpStatusCode.BadRequest, "InvalidCode", ct).ConfigureAwait(false);

                    int couponsBefore = f.Fakes.Stripe.Coupons.Count;
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/membership/stripe/checkout", member.AccessToken, new { code }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(couponsBefore + 1, f.Fakes.Stripe.Coupons.Count, "Coupon created");
                    StripeCouponRequest coupon = f.Fakes.Stripe.Coupons[f.Fakes.Stripe.Coupons.Count - 1];
                    Check.Equal(40, coupon.PercentOff, "Coupon percent");
                    Check.True(!coupon.Forever, "Coupon once");
                    Check.Equal(id, coupon.SignupCodeId, "Coupon metadata");
                    string couponId = f.Fakes.Stripe.LastCheckout!.CouponId ?? String.Empty;
                    Check.True(couponId.Length > 0, "Checkout carries the coupon");
                    Check.Equal(id, f.Fakes.Stripe.LastCheckout.SignupCodeId, "Checkout carries the code");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/membership/stripe/checkout", member.AccessToken, new { code }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(couponsBefore + 1, f.Fakes.Stripe.Coupons.Count, "Coupon reused");
                    Check.Equal(couponId, f.Fakes.Stripe.LastCheckout.CouponId, "Same coupon");
                    JsonElement withCoupon = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes/" + id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(couponId, Api.Str(withCoupon, "stripeCouponId"), "Coupon stored");
                    Check.Equal(0, withCoupon.GetProperty("redemptionCount").GetInt32(), "Not counted before payment");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/membership/stripe/checkout", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(f.Fakes.Stripe.LastCheckout.CouponId == null, "No coupon without a code");

                    string sub = "sub_disc_" + suffix;
                    f.Fakes.Stripe.Put(new StripeSubscription { Id = sub, CustomerId = "cus_disc_" + suffix, Status = "active", PriceId = "price_test_annual", CurrentPeriodStartUtc = DateTime.UtcNow, CurrentPeriodEndUtc = DateTime.UtcNow.AddYears(1) });
                    string payload = "{\"id\":\"evt_disc_" + suffix + "\",\"type\":\"checkout.session.completed\",\"subscriptionId\":\"" + sub + "\",\"customerId\":\"cus_disc_" + suffix + "\",\"clientReferenceId\":\"" + member.Id + "\",\"signupCodeId\":\"" + id + "\"}";
                    await StripeHookAsync(f, payload, "valid", HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await StripeHookAsync(f, payload.Replace("evt_disc_", "evt_disc2_", StringComparison.Ordinal), "valid", HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement used = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes/" + id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, used.GetProperty("redemptionCount").GetInt32(), "One redemption after two webhooks");
                    Check.True((await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("membership").GetProperty("isActive").GetBoolean(), "Paid membership active");
                    JsonElement again = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/validate", member.AccessToken, new { code }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!again.GetProperty("valid").GetBoolean(), "Already used");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/signup-codes/" + id, HttpFixture.AdminToken, new { discountPercent = 50 }, HttpStatusCode.Conflict, "CodeLocked", ct).ConfigureAwait(false);

                    JsonElement redemptions = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes/" + id + "/redemptions", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement redemption = redemptions.GetProperty("objects")[0];
                    Check.Equal("Active", Api.Str(redemption, "status"), "Redemption active");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-code-redemptions/" + Api.Str(redemption, "id") + "/revoke", HttpFixture.AdminToken, new { reason = "Test" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True((await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("membership").GetProperty("isActive").GetBoolean(), "Revoking a discount leaves the paid membership");

                    JsonElement batch = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes/batches", HttpFixture.AdminToken,
                        new { name = "Batch 25", quantity = 2, grantType = "Discount", discountPercent = 25, discountDuration = "Forever" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    JsonElement first = batch.GetProperty("codes")[0];
                    Check.Equal("25% off every renewal", Api.Str(first, "grantSummary"), "Batch summary");
                    Check.Equal(25, first.GetProperty("discountPercent").GetInt32(), "Batch percent");
                }),
                Case("Stripe.NotConfigured", "Stripe routes return 503 IntegrationNotConfigured when Stripe is off", async (f, ct) =>
                {
                    f.Fakes.Stripe.Configured = false;
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/membership/stripe/checkout", member.AccessToken, null, HttpStatusCode.ServiceUnavailable, "IntegrationNotConfigured", ct).ConfigureAwait(false);
                    await StripeHookAsync(f, "{}", "valid", HttpStatusCode.ServiceUnavailable, ct).ConfigureAwait(false);
                    JsonElement providers = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/providers", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!providers.GetProperty("stripe").GetProperty("enabled").GetBoolean(), "Stripe disabled");
                }),
                Case("Apple.VerifyAndNotifications", "App Store verification grants membership, rejects other accounts, and a REFUND notification ends it", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    TestMember other = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    Guid token = Guid.Parse(Api.Str(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false), "storeAccountToken"));
                    AppStoreTransaction transaction = new AppStoreTransaction { TransactionId = "2000001", OriginalTransactionId = "1000001", ProductId = f.Services.Property.AppStoreProductId,
                        PurchaseUtc = DateTime.UtcNow, ExpiresUtc = DateTime.UtcNow.AddYears(1), AppAccountToken = token, Environment = StoreEnvironmentEnum.Sandbox, RawPayloadJson = "{\"transactionId\":\"2000001\"}" };
                    f.Fakes.AppStore.PutTransaction("jws-1", transaction);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/membership/apple/verify", member.AccessToken, new { signedTransaction = "forged" }, HttpStatusCode.BadRequest, "InvalidTransaction", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/membership/apple/verify", other.AccessToken, new { signedTransaction = "jws-1" }, HttpStatusCode.Conflict, "TransactionBelongsToAnotherAccount", ct).ConfigureAwait(false);
                    JsonElement verified = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/membership/apple/verify", member.AccessToken, new { signedTransaction = "jws-1" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(verified.GetProperty("membership").GetProperty("isActive").GetBoolean(), "Active");
                    Check.Equal("Apple", Api.Str(verified.GetProperty("membership"), "store"), "Store");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/membership/apple/verify", member.AccessToken, new { signedTransaction = "jws-1" }, HttpStatusCode.OK, ct).ConfigureAwait(false);

                    AppStoreTransaction noToken = new AppStoreTransaction { TransactionId = "2000001", OriginalTransactionId = "1000001", ProductId = f.Services.Property.AppStoreProductId, PurchaseUtc = DateTime.UtcNow, ExpiresUtc = DateTime.UtcNow.AddYears(1) };
                    f.Fakes.AppStore.PutTransaction("jws-other", noToken);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/membership/apple/verify", other.AccessToken, new { signedTransaction = "jws-other" }, HttpStatusCode.Conflict, "TransactionBelongsToAnotherAccount", ct).ConfigureAwait(false);

                    f.Fakes.AppStore.PutNotification("payload-refund", new AppStoreNotification { NotificationUuid = "uuid-1", NotificationType = "REFUND", Transaction = transaction, RawPayloadJson = "{\"notificationType\":\"REFUND\"}" });
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/webhooks/apple", null, new { signedPayload = "forged" }, HttpStatusCode.BadRequest, "InvalidSignature", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/webhooks/apple", null, new { signedPayload = "payload-refund" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement after = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!after.GetProperty("membership").GetProperty("isActive").GetBoolean(), "Refund ends membership");
                    Check.Equal("Revoked", Api.Str(after.GetProperty("history").GetProperty("objects")[0], "status"), "History shows revoked");
                    JsonElement dup = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/webhooks/apple", null, new { signedPayload = "payload-refund" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Duplicate.", Api.Str(dup, "message"), "Idempotent");
                }),
                Case("Google.VerifyAndNotifications", "Google Play verification acknowledges and grants membership; RTDN pushes update it", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    string accountToken = Api.Str(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false), "storeAccountToken");
                    f.Fakes.GooglePlay.Put(new GooglePlaySubscription { PurchaseToken = "gp-token-1", ProductId = f.Services.Property.GooglePlayProductId, State = GooglePlaySubscriptionStateEnum.Active,
                        StartUtc = DateTime.UtcNow, ExpiryUtc = DateTime.UtcNow.AddYears(1), AutoRenewing = true, ObfuscatedAccountId = accountToken });
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/membership/google/verify", member.AccessToken, new { purchaseToken = "gp-token-1", productId = "other.product" }, HttpStatusCode.BadRequest, "InvalidPurchase", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/membership/google/verify", member.AccessToken, new { purchaseToken = "missing", productId = f.Services.Property.GooglePlayProductId }, HttpStatusCode.BadRequest, "InvalidPurchase", ct).ConfigureAwait(false);
                    JsonElement verified = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/membership/google/verify", member.AccessToken, new { purchaseToken = "gp-token-1", productId = f.Services.Property.GooglePlayProductId }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Google", Api.Str(verified.GetProperty("membership"), "store"), "Store");
                    Check.True(f.Fakes.GooglePlay.Acknowledged.Contains("gp-token-1"), "Acknowledged");
                    TestMember other = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/membership/google/verify", other.AccessToken, new { purchaseToken = "gp-token-1", productId = f.Services.Property.GooglePlayProductId }, HttpStatusCode.Conflict, "TransactionBelongsToAnotherAccount", ct).ConfigureAwait(false);

                    ApiResult badAuth = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/webhooks/google", null, "{\"messageId\":\"m1\",\"purchaseToken\":\"gp-token-1\"}", ct, "mobile", new Dictionary<string, string> { { "Authorization", "Bearer nope" } }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.Unauthorized, badAuth.Status, "Bad push token");
                    ApiResult test = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/webhooks/google", null, "{\"messageId\":\"m0\",\"test\":true}", ct, "mobile", new Dictionary<string, string> { { "Authorization", "Bearer valid" } }).ConfigureAwait(false);
                    Check.Equal("Ignored.", Api.Str(test.Body, "message"), "Test notification ignored");
                    f.Fakes.GooglePlay.Put(new GooglePlaySubscription { PurchaseToken = "gp-token-1", ProductId = f.Services.Property.GooglePlayProductId, State = GooglePlaySubscriptionStateEnum.Expired,
                        StartUtc = DateTime.UtcNow.AddYears(-1), ExpiryUtc = DateTime.UtcNow.AddMinutes(-5), ObfuscatedAccountId = accountToken, Acknowledged = true });
                    ApiResult push = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/webhooks/google", null, "{\"messageId\":\"m1\",\"purchaseToken\":\"gp-token-1\"}", ct, "mobile", new Dictionary<string, string> { { "Authorization", "Bearer valid" } }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, push.Status, "Push processed (body: " + push.Text + ")");
                    JsonElement after = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/membership", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!after.GetProperty("membership").GetProperty("isActive").GetBoolean(), "Expired");
                }),
                Case("Reconcile.Worker", "The reconciliation worker re-queries stores and corrects missed changes", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    f.Fakes.Stripe.Put(new StripeSubscription { Id = "sub_rec", CustomerId = "cus_rec", Status = "active", UserId = member.Id, CurrentPeriodStartUtc = DateTime.UtcNow, CurrentPeriodEndUtc = DateTime.UtcNow.AddYears(1) });
                    await StripeHookAsync(f, "{\"id\":\"evt_rec\",\"type\":\"customer.subscription.created\",\"subscriptionId\":\"sub_rec\",\"customerId\":\"cus_rec\"}", "valid", HttpStatusCode.OK, ct).ConfigureAwait(false);
                    f.Fakes.Stripe.Put(new StripeSubscription { Id = "sub_rec", CustomerId = "cus_rec", Status = "unpaid", UserId = member.Id, CurrentPeriodStartUtc = DateTime.UtcNow, CurrentPeriodEndUtc = DateTime.UtcNow.AddYears(1) });
                    SubscriptionReconciliationWorker worker = new SubscriptionReconciliationWorker(f.Database, f.Services.Purchases, f.Settings.Membership, f.Services.Logger);
                    await using (worker.ConfigureAwait(false))
                    {
                        int processed = await worker.RunOnceAsync(ct).ConfigureAwait(false);
                        Check.Equal(1, processed, "One subscription reconciled");
                    }
                    JsonElement list = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/subscriptions?status=InBillingRetry", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, list.GetProperty("totalRecords").GetInt64(), "Status corrected");
                    JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/subscriptions/" + Api.Str(list.GetProperty("objects")[0], "id"), HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(detail.GetProperty("events").GetProperty("objects").EnumerateArray().Any(e => Api.Str(e, "source") == "Reconcile"), "Reconcile event recorded");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/users/" + member.Id, member.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
                }),
                Case("Metrics.Membership", "Membership metrics count paid, free, stores, sources, and ARR", async (f, ct) =>
                {
                    TestMember paid = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    f.Fakes.Stripe.Put(new StripeSubscription { Id = "sub_m", CustomerId = "cus_m", Status = "active", UserId = paid.Id, CurrentPeriodStartUtc = DateTime.UtcNow, CurrentPeriodEndUtc = DateTime.UtcNow.AddYears(1) });
                    await StripeHookAsync(f, "{\"id\":\"evt_m\",\"type\":\"invoice.paid\",\"subscriptionId\":\"sub_m\",\"customerId\":\"cus_m\"}", "valid", HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement m = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/metrics/membership", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(3L, m.GetProperty("totalMembers").GetInt64(), "Total");
                    Check.Equal(2L, m.GetProperty("activeMembers").GetInt64(), "Active");
                    Check.Equal(1L, m.GetProperty("paidActive").GetInt64(), "Paid");
                    Check.Equal(1L, m.GetProperty("freeActive").GetInt64(), "Free");
                    Check.Equal(1L, m.GetProperty("byStore").GetProperty("Stripe").GetInt64(), "By store");
                    Check.Equal(0L, m.GetProperty("byStore").GetProperty("Apple").GetInt64(), "Apple zero");
                    Check.Equal(1L, m.GetProperty("bySource").GetProperty("AdminComp").GetInt64(), "By source");
                    Check.Equal(3L, m.GetProperty("newMembers30d").GetInt64(), "New");
                    Check.Equal(f.Property.Membership.PriceUsdCents / 100m, m.GetProperty("estimatedArrUsd").GetDecimal(), "ARR");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static async Task<JsonElement> StripeHookAsync(HttpFixture f, string payload, string signature, HttpStatusCode expected, CancellationToken ct)
        {
            ApiResult result = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/webhooks/stripe", null, payload, ct, "mobile", new Dictionary<string, string> { { "Stripe-Signature", signature } }).ConfigureAwait(false);
            Check.Equal(expected, result.Status, "Stripe webhook (body: " + result.Text + ")");
            return result.Body;
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        #endregion
    }
}
