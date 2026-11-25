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
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Phase 1 sign-up codes over HTTP: admin management, member validation and redemption, anti-enumeration, rate
    /// limits, single-use races, locking, batches, outcomes, revocation, CSV export, and expiry reminders.
    /// </summary>
    public static class SignupCodeSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "SignupCodes";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Sign-up codes (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Admin.CreateAndList", "Admins create vanity and generated codes and list them with search, filters, and sort", async (f, ct) =>
                {
                    JsonElement vanity = await CreateAsync(f, new { code = " seminar2026 ", name = "Spring seminar 2026", description = "Thanks for coming", grantType = "DurationMonths", grantMonths = 12, maxRedemptions = 50, labels = new[] { "Seminar" }, tags = new Dictionary<string, string> { { "campaign", "spring" } } }, ct).ConfigureAwait(false);
                    Check.Equal("SEMINAR2026", Api.Str(vanity, "code"), "Normalized");
                    Check.Equal("Free membership for 12 months", Api.Str(vanity, "grantSummary"), "Grant summary");
                    Check.Equal(f.Services.Property.PublicBaseUrl + "/join?code=SEMINAR2026", Api.Str(vanity, "joinUrl"), "Join URL");
                    Check.Equal("Active", Api.Str(vanity, "status"), "Status");
                    Check.Equal("Seminar", vanity.GetProperty("labels")[0].GetString(), "Labels (formerly tags) keep the casing first used");
                    Check.Equal("spring", Api.Str(vanity.GetProperty("tags"), "campaign"), "Tags dictionary");
                    JsonElement generated = await CreateAsync(f, new { name = "Reviewer", grantType = "Lifetime" }, ct).ConfigureAwait(false);
                    string code = Api.Str(generated, "code");
                    Check.True(code.Length == f.Settings.SignupCodes.GeneratedLength && code.All(c => SignupCodeService.GeneratedAlphabet.Contains(c)), "Generated from the unambiguous alphabet");
                    Check.Equal("Lifetime free membership", Api.Str(generated, "grantSummary"), "Lifetime summary");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes", HttpFixture.AdminToken, new { code = "Seminar2026", name = "Dupe", grantType = "Lifetime" }, HttpStatusCode.Conflict, "CodeInUse", ct).ConfigureAwait(false);
                    ApiResult invalid = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes", HttpFixture.AdminToken, new { code = "no spaces!", grantType = "DurationMonths", grantMonths = 0, startsUtc = DateTime.UtcNow.AddDays(2), expiresUtc = DateTime.UtcNow.AddDays(1) }, ct).ConfigureAwait(false);
                    JsonElement errors = invalid.Body.GetProperty("fieldErrors");
                    foreach (string field in new[] { "code", "name", "grantMonths", "expiresUtc" }) Check.True(errors.TryGetProperty(field, out JsonElement _), "fieldErrors." + field);
                    await CreateAsync(f, new { code = "LATER-CODE", name = "Later", grantType = "Lifetime", startsUtc = DateTime.UtcNow.AddDays(5) }, ct).ConfigureAwait(false);

                    JsonElement page = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes?search=semin", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, page.GetProperty("totalRecords").GetInt64(), "Search");
                    JsonElement scheduled = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes?status=Scheduled", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("LATER-CODE", Api.Str(scheduled.GetProperty("objects")[0], "code"), "Status filter");
                    JsonElement tagged = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes?labels=seminar", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, tagged.GetProperty("totalRecords").GetInt64(), "Label filter (formerly the tag filter)");
                    Check.Equal(1L, (await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes?tag.campaign=spring", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("totalRecords").GetInt64(), "Tag filter");
                    JsonElement sorted = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes?ordering=NameAscending&maxResults=2", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Later", Api.Str(sorted.GetProperty("objects")[0], "name"), "Sorted by name");
                    Check.True(!sorted.GetProperty("endOfResults").GetBoolean() && sorted.GetProperty("recordsRemaining").GetInt64() == 1 && sorted.GetProperty("continuationToken").GetString() != null, "Paged");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes?ordering=Bogus", HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes?status=Nope", HttpFixture.AdminToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                }),
                Case("Member.ValidateAndRedeem", "Validate describes the grant; redeem grants membership once; every invalid reason looks the same", async (f, ct) =>
                {
                    f.Settings.SignupCodes.AttemptsPerHour = 100;
                    await CreateAsync(f, new { code = "GOODCODE1", name = "Good", grantType = "DurationMonths", grantMonths = 12 }, ct).ConfigureAwait(false);
                    JsonElement inactive = await CreateAsync(f, new { code = "OFFCODE1", name = "Off", grantType = "Lifetime" }, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes/" + Api.Str(inactive, "id") + "/deactivate", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await CreateAsync(f, new { code = "SOONCODE1", name = "Soon", grantType = "Lifetime", startsUtc = DateTime.UtcNow.AddDays(1) }, ct).ConfigureAwait(false);
                    JsonElement used = await CreateAsync(f, new { code = "ONCECODE1", name = "Once", grantType = "Lifetime", maxRedemptions = 1 }, ct).ConfigureAwait(false);

                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    JsonElement valid = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/validate", member.AccessToken, new { code = "goodcode1" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(valid.GetProperty("valid").GetBoolean(), "Valid");
                    Check.Equal("Good", Api.Str(valid, "name"), "Name");
                    DateTime ends = valid.GetProperty("membershipEndsUtc").GetDateTime().ToUniversalTime();
                    Check.True(Math.Abs((ends - DateTime.UtcNow.AddMonths(12)).TotalMinutes) < 5, "Ends in 12 months");
                    Check.True(!valid.TryGetProperty("note", out JsonElement _), "No note without a paid membership");

                    foreach (string bad in new[] { "NOSUCHCODE", "OFFCODE1", "SOONCODE1" })
                    {
                        JsonElement result = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/validate", member.AccessToken, new { code = bad }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(!result.GetProperty("valid").GetBoolean(), bad + " invalid");
                        Check.Equal(SignupCodeService.InvalidMessage, Api.Str(result, "message"), bad + " same message");
                    }

                    JsonElement redeemed = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", member.AccessToken, new { code = "GOODCODE1" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(redeemed.GetProperty("membership").GetProperty("isActive").GetBoolean(), "Active");
                    Check.Equal("SignupCode", Api.Str(redeemed.GetProperty("membership"), "source"), "Source");
                    Check.Equal("Free membership for 12 months", Api.Str(redeemed, "grantSummary"), "Grant summary");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + member.Id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", member.AccessToken, new { code = "GOODCODE1" }, HttpStatusCode.Conflict, "AlreadyRedeemed", ct).ConfigureAwait(false);
                    JsonElement again = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/validate", member.AccessToken, new { code = "GOODCODE1" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!again.GetProperty("valid").GetBoolean(), "Already redeemed is not valid");

                    TestMember second = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", second.AccessToken, new { code = "ONCECODE1" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    foreach (string bad in new[] { "NOSUCHCODE", "OFFCODE1", "SOONCODE1", "ONCECODE1" })
                    {
                        ApiResult result = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", (await Api.RegisterAsync(f, ct).ConfigureAwait(false)).AccessToken, new { code = bad }, ct).ConfigureAwait(false);
                        Check.Equal(HttpStatusCode.BadRequest, result.Status, bad + " status");
                        Check.Equal("InvalidCode", result.Error, bad + " error");
                        Check.Equal(SignupCodeService.InvalidMessage, Api.Str(result.Body, "message"), bad + " message");
                    }
                    JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?action=signupCode.redeem", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2L, audit.GetProperty("totalRecords").GetInt64(), "Redemptions audited");
                }),
                Case("Member.PaidMembershipNote", "With an active paid membership the free period starts when the paid one ends", async (f, ct) =>
                {
                    await CreateAsync(f, new { code = "AFTERPAID1", name = "After", grantType = "DurationMonths", grantMonths = 6 }, ct).ConfigureAwait(false);
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    DateTime paidEnd = DateTime.UtcNow.AddMonths(3);
                    f.Fakes.Stripe.Put(new StripeSubscription { Id = "sub_paid", CustomerId = "cus_paid", Status = "active", UserId = member.Id, CurrentPeriodStartUtc = DateTime.UtcNow, CurrentPeriodEndUtc = paidEnd });
                    ApiResult hook = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/webhooks/stripe", null, "{\"id\":\"evt_paid\",\"type\":\"customer.subscription.created\",\"subscriptionId\":\"sub_paid\",\"customerId\":\"cus_paid\"}", ct,
                        "mobile", new Dictionary<string, string> { { "Stripe-Signature", "valid" } }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, hook.Status, "Webhook");
                    JsonElement valid = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/validate", member.AccessToken, new { code = "AFTERPAID1" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(Api.Str(valid, "note").StartsWith("Your free membership starts when your current membership ends on ", StringComparison.Ordinal), "Note");
                    DateTime ends = valid.GetProperty("membershipEndsUtc").GetDateTime().ToUniversalTime();
                    Check.True(Math.Abs((ends - paidEnd.AddMonths(6)).TotalMinutes) < 1, "Free period follows the paid one");
                    JsonElement redeemed = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", member.AccessToken, new { code = "AFTERPAID1" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Subscription", Api.Str(redeemed.GetProperty("membership"), "source"), "Paid stays primary");
                    DateTime expires = redeemed.GetProperty("membership").GetProperty("expiresUtc").GetDateTime().ToUniversalTime();
                    Check.True(Math.Abs((expires - paidEnd.AddMonths(6)).TotalMinutes) < 1, "Coverage runs through the free period");
                }),
                Case("Member.RefundQueuedBenefit", "Refund policy revokes future grants, preserves active grants and neutral defaults, and is idempotent", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    AppStoreTransaction transaction = new AppStoreTransaction
                    {
                        TransactionId = "transaction-refund", OriginalTransactionId = "original-refund",
                        ProductId = "example.membership", PurchaseUtc = DateTime.UtcNow.AddDays(-1), ExpiresUtc = DateTime.UtcNow.AddMonths(1)
                    };
                    await f.Services.Purchases.ApplyAppleAsync(transaction, member.Id, SubscriptionStatusEnum.Active, true, ct).ConfigureAwait(false);
                    await CreateAsync(f, new { code = "REFUNDQUEUE", name = "Queued", grantType = "DurationMonths", grantMonths = 6 }, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", member.AccessToken, new { code = "REFUNDQUEUE" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await f.Services.Purchases.ApplyAppleAsync(transaction, member.Id, SubscriptionStatusEnum.Refunded, false, ct).ConfigureAwait(false);
                    List<SignupCodeRedemption> redemptions = await f.Services.Database.SignupCodeRedemptions.EnumerateByUserAfterAsync(member.Id, null, 100, ct).ConfigureAwait(false);
                    Check.True(redemptions.Single().RevokedUtc == null, "Neutral default preserves queued benefit");
                    f.Settings.SignupCodes.CancelQueuedOnRefund = true;
                    await f.Services.Purchases.ApplyAppleAsync(transaction, member.Id, SubscriptionStatusEnum.Refunded, false, ct).ConfigureAwait(false);
                    SignupCodeRedemption revoked = (await f.Services.Database.SignupCodeRedemptions.ReadAsync(redemptions[0].Id, ct).ConfigureAwait(false))!;
                    Check.True(revoked.RevokedUtc != null, "Refund revokes future redemption");
                    Entitlement entitlement = (await f.Services.Database.Entitlements.ReadAsync(revoked.EntitlementId!, ct).ConfigureAwait(false))!;
                    Check.True(entitlement.RevokedUtc != null, "Future entitlement revoked");
                    await f.Services.Purchases.ApplyAppleAsync(transaction, member.Id, SubscriptionStatusEnum.Refunded, false, ct).ConfigureAwait(false);
                    Check.Equal(revoked.RevokedUtc, (await f.Services.Database.SignupCodeRedemptions.ReadAsync(revoked.Id, ct).ConfigureAwait(false))!.RevokedUtc, "Repeated refund keeps original revocation");
                    TestMember active = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await CreateAsync(f, new { code = "ACTIVEGRANT", name = "Active", grantType = "DurationMonths", grantMonths = 1 }, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", active.AccessToken, new { code = "ACTIVEGRANT" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await f.Services.SignupCodes.CancelQueuedOnRefundAsync(active.Id, ct).ConfigureAwait(false);
                    Check.True((await f.Services.Database.SignupCodeRedemptions.EnumerateByUserAfterAsync(active.Id, null, 100, ct).ConfigureAwait(false)).Single().RevokedUtc == null, "Active independent grant preserved");
                }),
                Case("Member.SingleUseRace", "Concurrent redemptions of a single-use code: exactly one member wins", async (f, ct) =>
                {
                    f.Settings.SignupCodes.AttemptsPerHour = 100;
                    JsonElement code = await CreateAsync(f, new { code = "RACECODE1", name = "Race", grantType = "Lifetime", maxRedemptions = 1 }, ct).ConfigureAwait(false);
                    List<TestMember> members = new List<TestMember>();
                    for (int i = 0; i < 8; i++) members.Add(await Api.RegisterAsync(f, ct).ConfigureAwait(false));
                    ApiResult[] results = await Task.WhenAll(members.Select(m => Api.SendAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", m.AccessToken, new { code = "RACECODE1" }, ct))).ConfigureAwait(false);
                    Check.Equal(1, results.Count(r => r.Status == HttpStatusCode.OK), "One winner");
                    Check.True(results.Where(r => r.Status != HttpStatusCode.OK).All(r => r.Error == "InvalidCode"), "Losers get InvalidCode");
                    JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes/" + Api.Str(code, "id"), HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, detail.GetProperty("redemptionCount").GetInt32(), "Count is 1");
                    Check.Equal("Exhausted", Api.Str(detail, "status"), "Exhausted");
                }),
                Case("Member.SameMemberRace", "Concurrent redemptions of one code by one member record a single redemption", async (f, ct) =>
                {
                    await CreateAsync(f, new { code = "OPENCODE1", name = "Open", grantType = "Lifetime" }, ct).ConfigureAwait(false);
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    f.Settings.SignupCodes.AttemptsPerHour = 100;
                    ApiResult[] results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Api.SendAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", member.AccessToken, new { code = "OPENCODE1" }, ct))).ConfigureAwait(false);
                    Check.Equal(1, results.Count(r => r.Status == HttpStatusCode.OK), "One success");
                    Check.True(results.Where(r => r.Status != HttpStatusCode.OK).All(r => r.Error == "AlreadyRedeemed"), "Others AlreadyRedeemed");
                }),
                Case("Member.RateLimited", "Attempts beyond the hourly limit return 429 RateLimited", async (f, ct) =>
                {
                    f.Settings.SignupCodes.AttemptsPerHour = 3;
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    for (int i = 0; i < 3; i++) await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/validate", member.AccessToken, new { code = "GUESS" + i }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/signup-codes/validate", member.AccessToken, new { code = "GUESS9" }, (HttpStatusCode)429, "RateLimited", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", member.AccessToken, new { code = "GUESS9" }, (HttpStatusCode)429, "RateLimited", ct).ConfigureAwait(false);
                    TestMember sameIp = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/signup-codes/validate", sameIp.AccessToken, new { code = "GUESS10" }, (HttpStatusCode)429, "RateLimited", ct).ConfigureAwait(false);
                }),
                Case("Admin.UpdateLockAndDeactivate", "Code and grant lock after the first redemption; maxRedemptions cannot drop below the count; deactivation keeps memberships", async (f, ct) =>
                {
                    JsonElement code = await CreateAsync(f, new { code = "LOCKME01", name = "Lock", grantType = "DurationMonths", grantMonths = 1, maxRedemptions = 5 }, ct).ConfigureAwait(false);
                    string id = Api.Str(code, "id");
                    JsonElement renamed = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/signup-codes/" + id, HttpFixture.AdminToken, new { code = "LOCKME02", grantMonths = 2, name = "Renamed", maxRedemptions = (int?)null }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("LOCKME02", Api.Str(renamed, "code"), "Code editable before redemption");
                    Check.Equal(2, renamed.GetProperty("grantMonths").GetInt32(), "Grant editable");
                    Check.True(renamed.GetProperty("maxRedemptions").ValueKind == JsonValueKind.Null, "Null means unlimited");
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", member.AccessToken, new { code = "LOCKME02" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/signup-codes/" + id, HttpFixture.AdminToken, new { code = "LOCKME03" }, HttpStatusCode.Conflict, "CodeLocked", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/signup-codes/" + id, HttpFixture.AdminToken, new { grantType = "Lifetime" }, HttpStatusCode.Conflict, "CodeLocked", ct).ConfigureAwait(false);
                    ApiResult below = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/admin/signup-codes/" + id, HttpFixture.AdminToken, new { maxRedemptions = 0 }, ct).ConfigureAwait(false);
                    Check.Equal("ValidationFailed", below.Error, "maxRedemptions below count");
                    JsonElement off = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes/" + id + "/deactivate", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Inactive", Api.Str(off, "status"), "Inactive");
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + member.Id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement on = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/signup-codes/" + id, HttpFixture.AdminToken, new { isActive = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Active", Api.Str(on, "status"), "Reactivated");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes/suc_missing", HttpFixture.AdminToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                }),
                Case("Admin.Delete", "Unredeemed codes delete with their labels; redeemed codes return 409 CodeRedeemed and stay", async (f, ct) =>
                {
                    JsonElement fresh = await CreateAsync(f, new { code = "DELETEME1", name = "Delete", grantType = "Lifetime", labels = new[] { "temp" } }, ct).ConfigureAwait(false);
                    string freshId = Api.Str(fresh, "id");
                    await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/signup-codes/" + freshId, HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes/" + freshId, HttpFixture.AdminToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/signup-codes/" + freshId, HttpFixture.AdminToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                    await CreateAsync(f, new { code = "DELETEME1", name = "Reused", grantType = "Lifetime" }, ct).ConfigureAwait(false);

                    JsonElement used = await CreateAsync(f, new { code = "KEEPME01", name = "Keep", grantType = "Lifetime" }, ct).ConfigureAwait(false);
                    string usedId = Api.Str(used, "id");
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", member.AccessToken, new { code = "KEEPME01" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/signup-codes/" + usedId, HttpFixture.AdminToken, null, HttpStatusCode.Conflict, "CodeRedeemed", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes/" + usedId, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/signup-codes/" + usedId, member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?action=signupCode.delete", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, audit.GetProperty("totalRecords").GetInt64(), "Audited once");
                }),
                Case("Admin.Batch", "Batches create unique single-use codes", async (f, ct) =>
                {
                    JsonElement batch = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes/batches", HttpFixture.AdminToken,
                        new { name = "Students", quantity = 25, grantType = "DurationMonths", grantMonths = 3, labels = new[] { "students" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
                    JsonElement codes = batch.GetProperty("codes");
                    Check.Equal(25, codes.GetArrayLength(), "Quantity");
                    Check.Equal(25, codes.EnumerateArray().Select(c => Api.Str(c, "code")).Distinct().Count(), "Unique");
                    Check.True(codes.EnumerateArray().All(c => c.GetProperty("maxRedemptions").GetInt32() == 1 && Api.Str(c, "batchId") == Api.Str(batch, "batchId")), "Single use and batched");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes/batches", HttpFixture.AdminToken, new { name = "Bad", quantity = 1001, grantType = "Lifetime" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?action=signupCode.batch", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, audit.GetProperty("totalRecords").GetInt64(), "Audited");
                }),
                Case("Admin.DetailRevokeSummaryCsv", "Detail outcomes and daily counts, redemption list, revoke one and all, summary, and CSV exports", async (f, ct) =>
                {
                    JsonElement code = await CreateAsync(f, new { code = "TRACKME01", name = "Track", grantType = "DurationMonths", grantMonths = 12 }, ct).ConfigureAwait(false);
                    string id = Api.Str(code, "id");
                    TestMember first = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    TestMember second = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", first.AccessToken, new { code = "TRACKME01" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", second.AccessToken, new { code = "TRACKME01" }, HttpStatusCode.OK, ct).ConfigureAwait(false);

                    JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes/" + id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement days = detail.GetProperty("redemptionsByDay");
                    Check.Equal(90, days.GetArrayLength(), "90 days");
                    Check.Equal(2L, days[89].GetProperty("count").GetInt64(), "Today");
                    Check.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), Api.Str(days[89], "date"), "Today's date");
                    Check.Equal(2L, detail.GetProperty("outcomes").GetProperty("active").GetInt64(), "Active outcome");

                    JsonElement redemptions = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes/" + id + "/redemptions?ordering=RedeemedDescending", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2L, redemptions.GetProperty("totalRecords").GetInt64(), "Two redemptions");
                    JsonElement item = redemptions.GetProperty("objects").EnumerateArray().First(r => Api.Str(r, "userId") == first.Id);
                    Check.True(item.GetProperty("isNewAccount").GetBoolean(), "Redeemed right after sign-up");
                    Check.Equal(first.Email, Api.Str(item, "memberEmail"), "Member email");

                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/signup-code-redemptions/" + Api.Str(item, "id") + "/revoke", HttpFixture.AdminToken, new { }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement revoked = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-code-redemptions/" + Api.Str(item, "id") + "/revoke", HttpFixture.AdminToken, new { reason = "Shared publicly" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Revoked", Api.Str(revoked, "status"), "Revoked");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/users/" + first.Id, first.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
                    Check.Equal(1, f.Fakes.Email.To(first.Email, EmailService.CodeRevokedCategory).Count, "Member notified");
                    JsonElement all = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes/" + id + "/revoke-all", HttpFixture.AdminToken, new { reason = "Campaign over" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, all.GetProperty("revokedCount").GetInt32(), "One more revoked");
                    JsonElement filtered = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes/" + id + "/redemptions?status=Revoked", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2L, filtered.GetProperty("totalRecords").GetInt64(), "Status filter");

                    JsonElement summary = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes/summary", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, summary.GetProperty("activeCodes").GetInt64(), "Active codes");
                    Check.Equal(2L, summary.GetProperty("totalRedemptions").GetInt64(), "Total redemptions");
                    Check.Equal(0L, summary.GetProperty("activeFreeMembers").GetInt64(), "No active free members after revocation");
                    double rate = summary.GetProperty("conversionRate").GetDouble();
                    Check.True(rate >= 0 && rate <= 1, "Conversion rate is a fraction");

                    ApiResult csv = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes/export.csv", HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
                    Check.True((csv.ContentType ?? String.Empty).StartsWith("text/csv", StringComparison.Ordinal), "CSV content type");
                    Check.True(csv.Text.StartsWith("id,code,name,status", StringComparison.Ordinal) && csv.Text.Contains("TRACKME01", StringComparison.Ordinal), "Codes CSV");
                    ApiResult redemptionCsv = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/admin/signup-codes/" + id + "/redemptions/export.csv", HttpFixture.AdminToken, null, ct).ConfigureAwait(false);
                    Check.Equal(3, redemptionCsv.Text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length, "Header plus two rows");
                }),
                Case("Worker.ExpiryReminders", "The reminder worker emails once at 14 days and once at 3 days, and skips members who stay active", async (f, ct) =>
                {
                    await CreateAsync(f, new { code = "REMIND001", name = "Remind", grantType = "DurationMonths", grantMonths = 1 }, ct).ConfigureAwait(false);
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    TestMember staying = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", member.AccessToken, new { code = "REMIND001" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/signup-codes/redeem", staying.AccessToken, new { code = "REMIND001" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.GrantCompAsync(f, staying.Id, ct).ConfigureAwait(false);
                    await SetEndAsync(f, member.Id, DateTime.UtcNow.AddDays(10), ct).ConfigureAwait(false);
                    await SetEndAsync(f, staying.Id, DateTime.UtcNow.AddDays(10), ct).ConfigureAwait(false);
                    SignupCodeReminderWorker worker = new SignupCodeReminderWorker(f.Database, f.Services.Membership, f.Services.Email, f.Settings.SignupCodes, f.Settings.Membership, f.Services.Logger);
                    await using (worker.ConfigureAwait(false))
                    {
                        Check.Equal(1, await worker.RunOnceAsync(ct).ConfigureAwait(false), "First reminder");
                        Check.Equal(0, await worker.RunOnceAsync(ct).ConfigureAwait(false), "Not repeated");
                        await SetEndAsync(f, member.Id, DateTime.UtcNow.AddDays(2), ct).ConfigureAwait(false);
                        Check.Equal(1, await worker.RunOnceAsync(ct).ConfigureAwait(false), "Final reminder");
                        Check.Equal(0, await worker.RunOnceAsync(ct).ConfigureAwait(false), "Final not repeated");
                    }
                    Check.Equal(2, f.Fakes.Email.To(member.Email, EmailService.CodeReminderCategory).Count, "Two reminders");
                    Check.Equal(0, f.Fakes.Email.To(staying.Email, EmailService.CodeReminderCategory).Count, "Covered member skipped");
                    Check.True(f.Fakes.Email.To(member.Email, EmailService.CodeReminderCategory)[0].TextBody.Contains("/app/membership", StringComparison.Ordinal), "Links to membership");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static async Task<JsonElement> CreateAsync(HttpFixture f, object body, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/signup-codes", HttpFixture.AdminToken, body, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }

        private static async Task SetEndAsync(HttpFixture f, string userId, DateTime endsUtc, CancellationToken ct)
        {
            List<SignupCodeRedemption> redemptions = await f.Database.SignupCodeRedemptions.EnumerateByUserAsync(userId, ct).ConfigureAwait(false);
            foreach (SignupCodeRedemption redemption in redemptions)
            {
                redemption.MembershipEndsUtc = endsUtc;
                await f.Database.SignupCodeRedemptions.UpdateAsync(redemption, ct).ConfigureAwait(false);
                Entitlement? entitlement = await f.Database.Entitlements.ReadAsync(redemption.EntitlementId, ct).ConfigureAwait(false);
                entitlement!.EndsUtc = endsUtc;
                await f.Database.Entitlements.UpdateAsync(entitlement, ct).ConfigureAwait(false);
            }
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        #endregion
    }
}
