namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Phase 1 rules without I/O: effective membership, store status mapping, route groups and the paywall, field
    /// validation, rate limiting, image sniffing, CSV escaping, and request history body redaction.
    /// </summary>
    public static class MembershipRulesSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Rules";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            DateTime now = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
            return new TestSuiteDescriptor(Id, "Phase 1 rules", new[]
            {
                Case("Membership.Compute", "Active when any source is active; latest end wins; null is lifetime; paid is primary", () =>
                {
                    Check.True(!MembershipService.Compute(new List<Entitlement>(), new List<Subscription>(), now).IsActive, "None");
                    Subscription sub = new Subscription { Id = "sub_1", Store = StoreEnum.Apple, AutoRenew = true };
                    List<Entitlement> ents = new List<Entitlement>
                    {
                        Ent(MembershipSourceEnum.Subscription, "sub_1", now.AddDays(-10), now.AddDays(20)),
                        Ent(MembershipSourceEnum.AdminComp, "c", now.AddDays(-1), now.AddDays(60)),
                        Ent(MembershipSourceEnum.SignupCode, "r", now.AddDays(-100), now.AddDays(-50))
                    };
                    MembershipSummary summary = MembershipService.Compute(ents, new List<Subscription> { sub }, now);
                    Check.True(summary.IsActive, "Active");
                    Check.Equal(MembershipSourceEnum.Subscription, summary.Source, "Paid primary");
                    Check.Equal(StoreEnum.Apple, summary.Store, "Store");
                    Check.True(summary.AutoRenew, "Auto-renew");
                    Check.Equal(now.AddDays(60), summary.ExpiresUtc, "Latest end wins");
                    ents.Add(Ent(MembershipSourceEnum.SignupCode, "life", now.AddDays(-1), null));
                    Check.True(MembershipService.Compute(ents, new List<Subscription> { sub }, now).ExpiresUtc == null, "Lifetime");
                    List<Entitlement> revoked = new List<Entitlement> { Ent(MembershipSourceEnum.AdminComp, "c", now.AddDays(-1), null) };
                    revoked[0].RevokedUtc = now.AddMinutes(-1);
                    Check.True(!MembershipService.Compute(revoked, new List<Subscription>(), now).IsActive, "Revoked is inactive");
                    List<Entitlement> future = new List<Entitlement> { Ent(MembershipSourceEnum.SignupCode, "r", now.AddDays(5), now.AddDays(30)) };
                    Check.True(!MembershipService.Compute(future, new List<Subscription>(), now).IsActive, "Future only is inactive");
                    List<Entitlement> free = new List<Entitlement> { Ent(MembershipSourceEnum.SignupCode, "r", now.AddDays(-5), now.AddDays(30)), Ent(MembershipSourceEnum.Subscription, "sub_1", now.AddDays(30), now.AddDays(395)) };
                    MembershipSummary converting = MembershipService.Compute(free, new List<Subscription> { sub }, now);
                    Check.True(converting.Source == MembershipSourceEnum.SignupCode && converting.WillConvertToPaid, "Will convert to paid");
                    return Task.CompletedTask;
                }),
                Case("Membership.CoverageChain", "Coverage follows back-to-back entitlements and stops at gaps", () =>
                {
                    List<Entitlement> ents = new List<Entitlement>
                    {
                        Ent(MembershipSourceEnum.Subscription, "s", now.AddDays(-1), now.AddDays(10)),
                        Ent(MembershipSourceEnum.SignupCode, "a", now.AddDays(10), now.AddDays(40)),
                        Ent(MembershipSourceEnum.SignupCode, "b", now.AddDays(50), now.AddDays(90))
                    };
                    Check.Equal(now.AddDays(40), MembershipService.CoverageEnd(ents, now), "Chained to 40 days, gap stops");
                    Check.Equal(now.AddDays(10), MembershipService.PaidCoverageEnd(ents, now), "Paid end");
                    return Task.CompletedTask;
                }),
                Case("Stores.StatusMapping", "Stripe and Google states map to normalized statuses", () =>
                {
                    Check.Equal(SubscriptionStatusEnum.Active, PurchaseService.MapStripe("trialing"), "trialing");
                    Check.Equal(SubscriptionStatusEnum.InGrace, PurchaseService.MapStripe("past_due"), "past_due");
                    Check.Equal(SubscriptionStatusEnum.InBillingRetry, PurchaseService.MapStripe("unpaid"), "unpaid");
                    Check.Equal(SubscriptionStatusEnum.Expired, PurchaseService.MapStripe("canceled"), "canceled");
                    Check.Equal(SubscriptionStatusEnum.Paused, PurchaseService.MapStripe("paused"), "paused");
                    Check.Equal(SubscriptionStatusEnum.Active, PurchaseService.MapGoogle(GooglePlaySubscriptionStateEnum.Canceled, now.AddDays(1), now), "Canceled until expiry");
                    Check.Equal(SubscriptionStatusEnum.Expired, PurchaseService.MapGoogle(GooglePlaySubscriptionStateEnum.Canceled, now.AddDays(-1), now), "Canceled after expiry");
                    Check.Equal(SubscriptionStatusEnum.InBillingRetry, PurchaseService.MapGoogle(GooglePlaySubscriptionStateEnum.OnHold, null, now), "On hold");
                    Check.Equal(SubscriptionStatusEnum.Refunded, WebhookService.AppleStatus(new TrellisKit.Core.Integrations.Models.AppStoreNotification { NotificationType = "REFUND" }), "Apple refund");
                    Check.Equal(SubscriptionStatusEnum.InGrace, WebhookService.AppleStatus(new TrellisKit.Core.Integrations.Models.AppStoreNotification { NotificationType = "DID_FAIL_TO_RENEW", Subtype = "GRACE_PERIOD" }), "Apple grace");
                    return Task.CompletedTask;
                }),
                Case("Authorization.Groups", "Paths map to route groups and contexts to decisions", () =>
                {
                    Check.Equal(RouteGroupEnum.Admin, AuthorizationService.Classify("/api/v1/admin/members"), "admin");
                    Check.Equal(RouteGroupEnum.Admin, AuthorizationService.Classify("/api/v1/settings/restart"), "settings");
                    Check.Equal(RouteGroupEnum.SignedIn, AuthorizationService.Classify("/api/v1/auth/me"), "auth/me");
                    Check.Equal(RouteGroupEnum.SignedIn, AuthorizationService.Classify("/api/v1/membership"), "membership");
                    Check.Equal(RouteGroupEnum.SignedIn, AuthorizationService.Classify("/api/v1/me"), "me");
                    Check.Equal(RouteGroupEnum.Member, AuthorizationService.Classify("/api/v1/media2/x"), "unknown is member");
                    Check.Equal(RouteGroupEnum.Member, AuthorizationService.Classify("/api/v1/users/usr_1"), "users");
                    AuthorizationService authz = new AuthorizationService(TestProperty.LoadSample().Membership.MinimumAge);
                    RequestContext member = new RequestContext { IsAuthenticated = true, UserId = "usr_1", AgeAttested = true, HasMembership = false };
                    Check.Equal(402, authz.Authorize(member, "/api/v1/users/x").StatusCode, "Paywall");
                    Check.True(authz.Authorize(member, "/api/v1/membership/stripe/checkout").IsPermitted, "Signed-in route");
                    Check.Equal(403, authz.Authorize(member, "/api/v1/admin/audit").StatusCode, "Admin forbidden");
                    member.AgeAttested = false;
                    AuthorizationDecision attest = authz.Authorize(member, "/api/v1/signup-codes/redeem");
                    Check.Equal("AgeAttestationRequired", attest.Error, "Attestation for codes");
                    int minimumAge = TestProperty.LoadSample().Membership.MinimumAge;
                    Check.True(attest.Message.Contains(minimumAge + " or older", StringComparison.Ordinal), "The message quotes the property minimum age, not a fixed age");
                    Check.True(new AuthorizationService(21).Authorize(member, "/api/v1/signup-codes/redeem").Message.Contains("21 or older", StringComparison.Ordinal), "Another minimum age is quoted");
                    Check.True(authz.Authorize(member, "/api/v1/me/age-attestation").IsPermitted, "Attestation route allowed");
                    RequestContext admin = new RequestContext { IsAuthenticated = true, UserId = "usr_2", IsAdmin = true };
                    Check.True(authz.Authorize(admin, "/api/v1/users/x").IsPermitted, "SystemAdmin bypasses membership and age");
                    return Task.CompletedTask;
                }),
                Case("Validation.Fields", "Password, handle, country, and date of birth rules", () =>
                {
                    Check.True(Validators.Password("short", null) != null, "Too short");
                    Check.True(Validators.Password(new string('x', 129), null) != null, "Too long");
                    Check.True(Validators.Password("me@example.com", "ME@example.com") != null, "Equals email");
                    Check.True(Validators.Password("ten chars!", null) == null, "Ten characters ok");
                    Check.True(Validators.Handle("ab") != null && Validators.Handle("a-b-c") != null && Validators.Handle("a_b.c9") == null, "Handles");
                    Check.True(Validators.Country("US") == null && Validators.Country("ZZ") != null, "Countries");
                    Check.True(Validators.DateOfBirth("2030-01-01", now, out DateTime _) != null, "Future birth date");
                    Validators.DateOfBirth("2008-11-11", now, out DateTime eighteen);
                    Check.True(Validators.IsAdult(eighteen, now, 18), "Exactly 18");
                    Validators.DateOfBirth("2008-11-12", now, out DateTime almost);
                    Check.True(!Validators.IsAdult(almost, now, 18), "One day short");
                    Check.Equal("Free membership for 1 month", SignupCodeService.GrantSummary(GrantTypeEnum.DurationMonths, 1, null), "Singular");
                    Check.Equal("Free membership until March 1, 2027", SignupCodeService.GrantSummary(GrantTypeEnum.FixedEndDate, null, new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc)), "Fixed end");
                    Check.True(SignupCodeService.Generate(10).IndexOfAny(new[] { '0', 'O', '1', 'I', 'L' }) < 0, "Unambiguous alphabet");
                    return Task.CompletedTask;
                }),
                Case("Infrastructure.Helpers", "Rate limiter windows, image sniffing, CSV escaping, and body redaction", () =>
                {
                    SlidingWindowRateLimiter limiter = new SlidingWindowRateLimiter();
                    TimeSpan hour = TimeSpan.FromHours(1);
                    Check.True(limiter.TryAcquire("k", 2, hour, now) && limiter.TryAcquire("k", 2, hour, now) && !limiter.TryAcquire("k", 2, hour, now), "Limit");
                    Check.True(limiter.TryAcquire("k", 2, hour, now.AddHours(1).AddSeconds(1)), "Window slides");
                    Check.Equal("jpg", AvatarStore.SniffExtension(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 }), "JPEG");
                    Check.Equal("webp", AvatarStore.SniffExtension(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }), "WebP");
                    Check.True(AvatarStore.SniffExtension(new byte[] { 0x47, 0x49, 0x46, 0x38, 0, 0, 0, 0, 0, 0, 0, 0 }) == null, "GIF rejected");
                    Check.True(!AvatarStore.IsValidFileName("../usr_1_1.png") && AvatarStore.IsValidFileName("usr_abc_123.png"), "File names");
                    string csv = new CsvWriter().Row("a,b", "=SUM(A1)", "say \"hi\"", null, true).ToString();
                    Check.Equal("\"a,b\",'=SUM(A1),\"say \"\"hi\"\"\",,true\r\n", csv, "CSV escaping");
                    string? redacted = RequestHistoryCaptureService.RedactBody("{\"email\":\"a@b.c\",\"password\":\"secret\",\"nested\":{\"refreshToken\":\"r\"},\"list\":[{\"token\":\"t\"}]}", true);
                    Check.True(redacted != null && !redacted.Contains("secret", StringComparison.Ordinal) && !redacted.Contains("\"r\"", StringComparison.Ordinal) && !redacted.Contains("\"t\"", StringComparison.Ordinal) && redacted.Contains("a@b.c", StringComparison.Ordinal), "Redacted");
                    Check.True(RequestHistoryCaptureService.RedactBody("password=secret", true) == null, "Non-JSON auth body dropped");
                    return Task.CompletedTask;
                })
            });
        }

        #endregion

        #region Private-Methods

        private static Entitlement Ent(MembershipSourceEnum source, string sourceId, DateTime starts, DateTime? ends)
        {
            return new Entitlement { Id = "ent_" + sourceId + "_" + Guid.NewGuid().ToString("N").Substring(0, 6), Source = source, SourceId = sourceId, StartsUtc = starts, EndsUtc = ends };
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<Task> body)
        {
            return SuiteCases.Unit(Id, caseId, name, ct => body());
        }

        #endregion
    }
}
