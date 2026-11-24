namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Phase 1 database contract: identity, sessions, tokens, roles, profiles, subscriptions, entitlements, sign-up
    /// codes, and audit. Runs against the provider selected by TK_TEST_PROVIDER, including race cases for single-use
    /// redemption and refresh token rotation.
    /// </summary>
    public static class IdentityDatabaseSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "IdentityDatabase";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Identity and membership database contract (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Users.UniqueAndFilters", "Email and handle are unique case-insensitively; lookups and list filters work", async (db, ct) =>
                {
                    User a = await NewUserAsync(db, "Alice@Example.com", "Alice O'Neil", ct).ConfigureAwait(false);
                    a.Handle = "Alice.1";
                    a.Rank = "Intermediate";
                    await db.Driver.Users.UpdateAsync(a, ct).ConfigureAwait(false);
                    await Check.ThrowsAsync<DuplicateRecordException>(() => NewUserAsync(db, "alice@EXAMPLE.com", "Copy", ct), "Duplicate email").ConfigureAwait(false);
                    User b = await NewUserAsync(db, "bob@example.com", "Bob", ct).ConfigureAwait(false);
                    b.Handle = "ALICE.1";
                    await Check.ThrowsAsync<DuplicateRecordException>(() => db.Driver.Users.UpdateAsync(b, ct), "Duplicate handle").ConfigureAwait(false);
                    Check.Equal(a.Id, (await db.Driver.Users.ReadByEmailAsync("ALICE@example.COM", ct).ConfigureAwait(false))!.Id, "Email lookup");
                    Check.Equal(a.Id, (await db.Driver.Users.ReadByHandleAsync("alice.1", ct).ConfigureAwait(false))!.Id, "Handle lookup");
                    Check.Equal(a.Id, (await db.Driver.Users.ReadByStoreAccountTokenAsync(a.StoreAccountToken.ToUpperInvariant(), ct).ConfigureAwait(false))!.Id, "Store token lookup");
                    User? read = await db.Driver.Users.ReadAsync(a.Id, ct).ConfigureAwait(false);
                    Check.Equal("Alice O'Neil", read!.DisplayName, "Quote round-trip");
                    Check.Equal("Intermediate", read.Rank, "Rank round-trip");
                    Check.True(read.CanUploadVideo && !read.CanPostToFeed, "Flag defaults");

                    await GrantAsync(db, b.Id, MembershipSourceEnum.AdminComp, null, ct).ConfigureAwait(false);
                    await db.Driver.RoleAssignments.AddAsync(new RoleAssignment { Id = IdGenerator.GenerateRoleAssignmentId(), UserId = a.Id, Role = RoleEnum.SystemAdmin }, ct).ConfigureAwait(false);
                    DateTime now = DateTime.UtcNow;
                    Check.Equal(1L, (await db.Driver.Users.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.AdminMembers, new EnumerationQuery { Search = "o'neil" }), new UserFilter(), now, ct).ConfigureAwait(false)).TotalRecords, "Search with a quote");
                    Check.Equal(0L, (await db.Driver.Users.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.AdminMembers, new EnumerationQuery { Search = "%" }), new UserFilter(), now, ct).ConfigureAwait(false)).TotalRecords, "LIKE wildcards escaped");
                    Check.Equal(1L, (await db.Driver.Users.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.AdminMembers), new UserFilter { Rank = "Intermediate" }, now, ct).ConfigureAwait(false)).TotalRecords, "Rank");
                    Check.Equal(b.Id, (await db.Driver.Users.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.AdminMembers), new UserFilter { Membership = MembershipFilterEnum.Free }, now, ct).ConfigureAwait(false)).Objects.Single().Id, "Free");
                    Check.Equal(a.Id, (await db.Driver.Users.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.AdminMembers), new UserFilter { Membership = MembershipFilterEnum.Inactive }, now, ct).ConfigureAwait(false)).Objects.Single().Id, "Inactive");
                    Check.Equal(a.Id, (await db.Driver.Users.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.AdminMembers), new UserFilter { Role = RoleEnum.SystemAdmin }, now, ct).ConfigureAwait(false)).Objects.Single().Id, "Role");
                    EnumerationResult<User> sorted = await db.Driver.Users.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.AdminMembers, 25, 0, "LastSeenDescending"), new UserFilter(), now, ct).ConfigureAwait(false);
                    Check.Equal(2, sorted.Objects.Count, "Sort with nulls");
                    Check.Equal(2L, await db.Driver.Users.CountAsync(null, ct).ConfigureAwait(false), "Count");
                    Check.Equal(2, (await db.Driver.Users.ReadManyAsync(new[] { a.Id, b.Id, "usr_missing" }, ct).ConfigureAwait(false)).Count, "Read many");
                }),
                Case("RefreshTokens.ConcurrentRotation", "Exactly one of many concurrent rotations of a refresh token succeeds", async (db, ct) =>
                {
                    User user = await NewUserAsync(db, "rot@example.com", "Rot", ct).ConfigureAwait(false);
                    AuthSession session = await db.Driver.Sessions.CreateAsync(new AuthSession { Id = IdGenerator.GenerateSessionId(), UserId = user.Id, ClientType = ClientTypeEnum.Mobile, ExpiresUtc = DateTime.UtcNow.AddDays(1) }, ct).ConfigureAwait(false);
                    RefreshToken token = await db.Driver.RefreshTokens.CreateAsync(new RefreshToken { Id = IdGenerator.GenerateRefreshTokenId(), SessionId = session.Id, UserId = user.Id, TokenHash = "hash-1", ExpiresUtc = DateTime.UtcNow.AddDays(1) }, ct).ConfigureAwait(false);
                    bool[] results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => db.Driver.RefreshTokens.TryMarkUsedAsync(token.Id, "rft_next_" + i, DateTime.UtcNow, ct))).ConfigureAwait(false);
                    Check.Equal(1, results.Count(r => r), "One winner");
                    RefreshToken? used = await db.Driver.RefreshTokens.ReadByHashAsync("hash-1", ct).ConfigureAwait(false);
                    Check.True(used!.UsedUtc != null && used.ReplacedById != null, "Marked used");
                    await Check.ThrowsAsync<DuplicateRecordException>(() => db.Driver.RefreshTokens.CreateAsync(new RefreshToken { Id = IdGenerator.GenerateRefreshTokenId(), SessionId = session.Id, UserId = user.Id, TokenHash = "hash-1", ExpiresUtc = DateTime.UtcNow }, ct), "Hash unique").ConfigureAwait(false);
                }),
                Case("Sessions.RevokeAll", "Sessions revoke individually and in bulk with an exception", async (db, ct) =>
                {
                    User user = await NewUserAsync(db, "ses@example.com", "Ses", ct).ConfigureAwait(false);
                    List<AuthSession> sessions = new List<AuthSession>();
                    for (int i = 0; i < 3; i++)
                        sessions.Add(await db.Driver.Sessions.CreateAsync(new AuthSession { Id = IdGenerator.GenerateSessionId(), UserId = user.Id, SourceIp = "10.0.0." + i, ExpiresUtc = DateTime.UtcNow.AddDays(1) }, ct).ConfigureAwait(false));
                    Check.True(await db.Driver.Sessions.RevokeAsync(sessions[0].Id, "test", DateTime.UtcNow, ct).ConfigureAwait(false), "Revoked");
                    Check.True(!await db.Driver.Sessions.RevokeAsync(sessions[0].Id, "test", DateTime.UtcNow, ct).ConfigureAwait(false), "Only once");
                    List<string> bulk = await db.Driver.Sessions.RevokeAllForUserAsync(user.Id, sessions[2].Id, "bulk", DateTime.UtcNow, ct).ConfigureAwait(false);
                    Check.Equal(sessions[1].Id, bulk.Single(), "Bulk spares the kept session");
                    List<AuthSession> active = await db.Driver.Sessions.EnumerateActiveByUserAsync(user.Id, DateTime.UtcNow, ct).ConfigureAwait(false);
                    Check.Equal(sessions[2].Id, active.Single().Id, "One active");
                    await db.Driver.Sessions.TouchAsync(sessions[2].Id, DateTime.UtcNow.AddMinutes(1), ct).ConfigureAwait(false);
                }),
                Case("OneTimeTokens.ConsumeOnce", "One-time tokens are consumed once, never after expiry, and invalidate by purpose", async (db, ct) =>
                {
                    User user = await NewUserAsync(db, "ott@example.com", "Ott", ct).ConfigureAwait(false);
                    OneTimeToken token = await db.Driver.OneTimeTokens.CreateAsync(new OneTimeToken { Id = IdGenerator.GenerateOneTimeTokenId(), UserId = user.Id, TokenHash = "ott-1", Email = user.Email, ExpiresUtc = DateTime.UtcNow.AddHours(1) }, ct).ConfigureAwait(false);
                    bool[] results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => db.Driver.OneTimeTokens.TryConsumeAsync(token.Id, DateTime.UtcNow, ct))).ConfigureAwait(false);
                    Check.Equal(1, results.Count(r => r), "Consumed once");
                    OneTimeToken expired = await db.Driver.OneTimeTokens.CreateAsync(new OneTimeToken { Id = IdGenerator.GenerateOneTimeTokenId(), UserId = user.Id, TokenHash = "ott-2", ExpiresUtc = DateTime.UtcNow.AddSeconds(-1) }, ct).ConfigureAwait(false);
                    Check.True(!await db.Driver.OneTimeTokens.TryConsumeAsync(expired.Id, DateTime.UtcNow, ct).ConfigureAwait(false), "Expired");
                    await db.Driver.OneTimeTokens.CreateAsync(new OneTimeToken { Id = IdGenerator.GenerateOneTimeTokenId(), UserId = user.Id, Purpose = OneTimeTokenPurposeEnum.ResetPassword, TokenHash = "ott-3", ExpiresUtc = DateTime.UtcNow.AddHours(1) }, ct).ConfigureAwait(false);
                    Check.Equal(1, await db.Driver.OneTimeTokens.InvalidateAsync(user.Id, OneTimeTokenPurposeEnum.ResetPassword, DateTime.UtcNow, ct).ConfigureAwait(false), "Invalidated by purpose");
                    Check.Equal(OneTimeTokenPurposeEnum.ResetPassword, (await db.Driver.OneTimeTokens.ReadByHashAsync("ott-3", ct).ConfigureAwait(false))!.Purpose, "Purpose round-trip");
                }),
                Case("Profile.RolesRankHistoryGoalsIdentities", "Roles, rankHistory, goals, and identities replace and enforce uniqueness", async (db, ct) =>
                {
                    User user = await NewUserAsync(db, "prof@example.com", "Prof", ct).ConfigureAwait(false);
                    await db.Driver.RoleAssignments.ReplaceForUserAsync(user.Id, new List<RoleAssignment>
                    {
                        new RoleAssignment { Id = IdGenerator.GenerateRoleAssignmentId(), Role = RoleEnum.Moderator, ResourceType = ResourceTypeEnum.Forum, ResourceId = "frm_1" },
                        new RoleAssignment { Id = IdGenerator.GenerateRoleAssignmentId(), Role = RoleEnum.Moderator, ResourceType = ResourceTypeEnum.Forum, ResourceId = "frm_1" }
                    }, ct).ConfigureAwait(false);
                    Check.Equal(1, (await db.Driver.RoleAssignments.EnumerateByUserAsync(user.Id, ct).ConfigureAwait(false)).Count, "Duplicates collapsed");
                    Check.True(!await db.Driver.RoleAssignments.AddAsync(new RoleAssignment { Id = IdGenerator.GenerateRoleAssignmentId(), UserId = user.Id, Role = RoleEnum.Moderator, ResourceType = ResourceTypeEnum.Forum, ResourceId = "frm_1" }, ct).ConfigureAwait(false), "Add ignores duplicates");
                    await db.Driver.RankHistory.ReplaceForUserAsync(user.Id, new List<RankHistoryEntry>
                    {
                        new RankHistoryEntry { Id = IdGenerator.GenerateRankHistoryEntryId(), Rank = "Advanced", AwardedByName = "Prof X", DateAwarded = "2015-06-01" },
                        new RankHistoryEntry { Id = IdGenerator.GenerateRankHistoryEntryId(), Rank = "Beginner" }
                    }, ct).ConfigureAwait(false);
                    List<RankHistoryEntry> rankHistory = await db.Driver.RankHistory.EnumerateByUserAsync(user.Id, ct).ConfigureAwait(false);
                    Check.True(rankHistory.Count == 2 && rankHistory[0].Rank == "Advanced" && rankHistory[0].DateAwarded == "2015-06-01" && rankHistory[1].Position == 1, "RankHistory order");
                    await db.Driver.Goals.ReplaceForUserAsync(user.Id, new List<string> { "Teaching", "Learning", "Teaching" }, ct).ConfigureAwait(false);
                    List<string> goals = await db.Driver.Goals.EnumerateByUserAsync(user.Id, ct).ConfigureAwait(false);
                    Check.True(goals.SequenceEqual(new[] { "Teaching", "Learning" }), "Goals ordered and distinct");
                    await db.Driver.ExternalIdentities.CreateAsync(new ExternalIdentity { Id = IdGenerator.GenerateExternalIdentityId(), UserId = user.Id, Provider = ExternalProviderEnum.Google, Subject = "g-1" }, ct).ConfigureAwait(false);
                    User other = await NewUserAsync(db, "other@example.com", "Other", ct).ConfigureAwait(false);
                    await Check.ThrowsAsync<DuplicateRecordException>(() => db.Driver.ExternalIdentities.CreateAsync(new ExternalIdentity { Id = IdGenerator.GenerateExternalIdentityId(), UserId = other.Id, Provider = ExternalProviderEnum.Google, Subject = "g-1" }, ct), "Subject unique per provider").ConfigureAwait(false);
                    await db.Driver.ExternalIdentities.CreateAsync(new ExternalIdentity { Id = IdGenerator.GenerateExternalIdentityId(), UserId = other.Id, Provider = ExternalProviderEnum.Apple, Subject = "g-1" }, ct).ConfigureAwait(false);
                    Check.Equal(user.Id, (await db.Driver.ExternalIdentities.ReadByProviderSubjectAsync(ExternalProviderEnum.Google, "g-1", ct).ConfigureAwait(false))!.UserId, "Lookup");
                    Check.True(await db.Driver.ExternalIdentities.DeleteAsync(user.Id, ExternalProviderEnum.Google, ct).ConfigureAwait(false), "Unlink");
                    await db.Driver.Credentials.UpsertAsync(new UserCredential { Id = IdGenerator.GenerateCredentialId(), UserId = user.Id, PasswordHash = "h1" }, ct).ConfigureAwait(false);
                    await db.Driver.Credentials.UpsertAsync(new UserCredential { Id = IdGenerator.GenerateCredentialId(), UserId = user.Id, PasswordHash = "h2" }, ct).ConfigureAwait(false);
                    Check.Equal("h2", (await db.Driver.Credentials.ReadByUserAsync(user.Id, ct).ConfigureAwait(false))!.PasswordHash, "Credential upsert");
                }),
                Case("Subscriptions.UniqueEventsAndCounts", "Store ids and event ids are unique; reconciliation and metrics queries count correctly", async (db, ct) =>
                {
                    DateTime now = DateTime.UtcNow;
                    User paid = await NewUserAsync(db, "paid@example.com", "Paid Person", ct).ConfigureAwait(false);
                    User free = await NewUserAsync(db, "free@example.com", "Free Person", ct).ConfigureAwait(false);
                    User lapsed = await NewUserAsync(db, "lapsed@example.com", "Lapsed Person", ct).ConfigureAwait(false);
                    Subscription sub = await db.Driver.Subscriptions.CreateAsync(new Subscription { Id = IdGenerator.GenerateSubscriptionId(), UserId = paid.Id, Store = StoreEnum.Apple, StoreSubscriptionId = "orig-1", ProductId = "p", CurrentPeriodEndUtc = now.AddDays(30), AutoRenew = true }, ct).ConfigureAwait(false);
                    await Check.ThrowsAsync<DuplicateRecordException>(() => db.Driver.Subscriptions.CreateAsync(new Subscription { Id = IdGenerator.GenerateSubscriptionId(), UserId = free.Id, Store = StoreEnum.Apple, StoreSubscriptionId = "orig-1", ProductId = "p" }, ct), "Store id unique").ConfigureAwait(false);
                    await db.Driver.Subscriptions.CreateAsync(new Subscription { Id = IdGenerator.GenerateSubscriptionId(), UserId = lapsed.Id, Store = StoreEnum.Stripe, StoreSubscriptionId = "sub_x", ProductId = "p", Status = SubscriptionStatusEnum.Expired }, ct).ConfigureAwait(false);
                    await GrantAsync(db, paid.Id, MembershipSourceEnum.Subscription, sub.Id, ct).ConfigureAwait(false);
                    await GrantAsync(db, free.Id, MembershipSourceEnum.SignupCode, null, ct).ConfigureAwait(false);
                    await db.Driver.Entitlements.CreateAsync(new Entitlement { Id = IdGenerator.GenerateEntitlementId(), UserId = lapsed.Id, Source = MembershipSourceEnum.AdminComp, SourceId = "x", StartsUtc = now.AddDays(-40), EndsUtc = now.AddDays(-2) }, ct).ConfigureAwait(false);

                    SubscriptionEvent ev = new SubscriptionEvent { Id = IdGenerator.GenerateSubscriptionEventId(), Store = StoreEnum.Apple, StoreEventId = "uuid-1", EventType = "DID_RENEW", RawPayloadJson = "{\"a\":\"it's\"}" };
                    Check.True(await db.Driver.SubscriptionEvents.TryCreateAsync(ev, ct).ConfigureAwait(false), "Inserted");
                    Check.True(!await db.Driver.SubscriptionEvents.TryCreateAsync(new SubscriptionEvent { Id = IdGenerator.GenerateSubscriptionEventId(), Store = StoreEnum.Apple, StoreEventId = "uuid-1", EventType = "x" }, ct).ConfigureAwait(false), "Duplicate ignored");
                    Check.True(await db.Driver.SubscriptionEvents.TryCreateAsync(new SubscriptionEvent { Id = IdGenerator.GenerateSubscriptionEventId(), Store = StoreEnum.Google, StoreEventId = "uuid-1", EventType = "x" }, ct).ConfigureAwait(false), "Unique per store");
                    ev.SubscriptionId = sub.Id;
                    ev.Outcome = "Processed";
                    await db.Driver.SubscriptionEvents.UpdateAsync(ev, ct).ConfigureAwait(false);
                    Check.Equal("{\"a\":\"it's\"}", (await db.Driver.SubscriptionEvents.EnumerateBySubscriptionAsync(sub.Id, 10, ct).ConfigureAwait(false)).Single().RawPayloadJson, "Raw payload round-trip");

                    Check.Equal(1, (await db.Driver.Subscriptions.EnumerateForReconciliationAsync(100, ct).ConfigureAwait(false)).Count, "Expired excluded from reconciliation");
                    Check.Equal(1L, (await db.Driver.Subscriptions.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.Subscriptions, new EnumerationQuery { Search = "paid@" }), new SubscriptionFilter(), ct).ConfigureAwait(false)).TotalRecords, "Search by member email");
                    Check.Equal(1L, (await db.Driver.Subscriptions.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.Subscriptions), new SubscriptionFilter { Status = SubscriptionStatusEnum.Expired }, ct).ConfigureAwait(false)).TotalRecords, "Status filter");
                    Check.Equal(2L, await db.Driver.Entitlements.CountActiveUsersAsync(now, null, ct).ConfigureAwait(false), "Active users");
                    Check.Equal(1L, await db.Driver.Entitlements.CountActiveUsersAsync(now, MembershipSourceEnum.Subscription, ct).ConfigureAwait(false), "Paid");
                    Check.Equal(1L, await db.Driver.Entitlements.CountFreeOnlyUsersAsync(now, ct).ConfigureAwait(false), "Free only");
                    Dictionary<StoreEnum, long> byStore = await db.Driver.Entitlements.CountActiveUsersByStoreAsync(now, ct).ConfigureAwait(false);
                    Check.True(byStore[StoreEnum.Apple] == 1 && byStore[StoreEnum.Stripe] == 0 && byStore[StoreEnum.Google] == 0, "By store");
                    Check.Equal(1L, await db.Driver.Entitlements.CountLapsedUsersAsync(now.AddDays(-30), now, ct).ConfigureAwait(false), "Lapsed");
                }),
                Case("SignupCodes.SingleUseRace", "Twenty members redeeming a single-use code concurrently: exactly one redemption", async (db, ct) =>
                {
                    DateTime now = DateTime.UtcNow;
                    SignupCode code = await db.Driver.SignupCodes.CreateAsync(new SignupCode { Id = IdGenerator.GenerateSignupCodeId(), Code = "RACE-1", Name = "Race", GrantType = GrantTypeEnum.Lifetime, MaxRedemptions = 1 }, ct).ConfigureAwait(false);
                    await db.Driver.Metadata.WriteAsync(TargetTypeEnum.SignupCode, code.Id, new ObjectMetadata { Labels = new List<string> { "Race", "x" } }, ct).ConfigureAwait(false);
                    List<User> users = new List<User>();
                    for (int i = 0; i < 20; i++) users.Add(await NewUserAsync(db, "racer" + i + "@example.com", "Racer " + i, ct).ConfigureAwait(false));
                    bool[] results = await Task.WhenAll(users.Select(u => RedeemAsync(db, code, u, now, ct))).ConfigureAwait(false);
                    Check.Equal(1, results.Count(r => r), "One redemption");
                    SignupCode? after = await db.Driver.SignupCodes.ReadByCodeAsync("race-1", ct).ConfigureAwait(false);
                    Check.Equal(1, after!.RedemptionCount, "Count");
                    Check.Equal(SignupCodeStatusEnum.Exhausted, after.StatusAt(now), "Exhausted");
                    Check.True((await db.Driver.Metadata.ReadAsync(TargetTypeEnum.SignupCode, code.Id, ct).ConfigureAwait(false)).Labels.OrderBy(l => l, StringComparer.Ordinal).SequenceEqual(new[] { "Race", "x" }), "Labels (the former tags)");
                    Check.Equal(1L, await db.Driver.SignupCodeRedemptions.CountAsync(code.Id, null, ct).ConfigureAwait(false), "One row");
                    Check.Equal(1L, await db.Driver.Entitlements.CountActiveUsersAsync(now, MembershipSourceEnum.SignupCode, ct).ConfigureAwait(false), "One entitlement");
                }),
                Case("SignupCodes.SameMemberRace", "One member redeeming an unlimited code concurrently gets one redemption", async (db, ct) =>
                {
                    DateTime now = DateTime.UtcNow;
                    SignupCode code = await db.Driver.SignupCodes.CreateAsync(new SignupCode { Id = IdGenerator.GenerateSignupCodeId(), Code = "OPEN-1", Name = "Open", GrantType = GrantTypeEnum.DurationMonths, GrantMonths = 1 }, ct).ConfigureAwait(false);
                    User user = await NewUserAsync(db, "same@example.com", "Same", ct).ConfigureAwait(false);
                    bool[] results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => RedeemAsync(db, code, user, now, ct))).ConfigureAwait(false);
                    Check.Equal(1, results.Count(r => r), "One redemption");
                    Check.Equal(1, (await db.Driver.SignupCodes.ReadAsync(code.Id, ct).ConfigureAwait(false))!.RedemptionCount, "Count not inflated");
                    Check.Equal(1, (await db.Driver.Entitlements.EnumerateByUserAsync(user.Id, ct).ConfigureAwait(false)).Count, "One entitlement");
                }),
                Case("SignupCodes.StatusOutcomesReminders", "Status filters, outcomes, daily counts, reminder stages, and uniqueness", async (db, ct) =>
                {
                    DateTime now = DateTime.UtcNow;
                    SignupCode active = await db.Driver.SignupCodes.CreateAsync(new SignupCode { Id = IdGenerator.GenerateSignupCodeId(), Code = "ACT-1", Name = "Active", GrantType = GrantTypeEnum.DurationMonths, GrantMonths = 1 }, ct).ConfigureAwait(false);
                    await db.Driver.SignupCodes.CreateAsync(new SignupCode { Id = IdGenerator.GenerateSignupCodeId(), Code = "OFF-1", Name = "Off", IsActive = false }, ct).ConfigureAwait(false);
                    await db.Driver.SignupCodes.CreateAsync(new SignupCode { Id = IdGenerator.GenerateSignupCodeId(), Code = "OLD-1", Name = "Old", ExpiresUtc = now.AddDays(-1) }, ct).ConfigureAwait(false);
                    await db.Driver.SignupCodes.CreateAsync(new SignupCode { Id = IdGenerator.GenerateSignupCodeId(), Code = "NEW-1", Name = "New", StartsUtc = now.AddDays(1) }, ct).ConfigureAwait(false);
                    await Check.ThrowsAsync<DuplicateRecordException>(() => db.Driver.SignupCodes.CreateAsync(new SignupCode { Id = IdGenerator.GenerateSignupCodeId(), Code = "act-1", Name = "Dupe" }, ct), "Code unique case-insensitively").ConfigureAwait(false);
                    foreach (SignupCodeStatusEnum status in new[] { SignupCodeStatusEnum.Active, SignupCodeStatusEnum.Inactive, SignupCodeStatusEnum.Expired, SignupCodeStatusEnum.Scheduled })
                        Check.Equal(1L, await db.Driver.SignupCodes.CountByStatusAsync(status, now, ct).ConfigureAwait(false), status + " count");
                    Check.Equal(1L, (await db.Driver.SignupCodes.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.SignupCodes), new SignupCodeFilter { Status = SignupCodeStatusEnum.Scheduled }, now, ct).ConfigureAwait(false)).TotalRecords, "Status filter");

                    User a = await NewUserAsync(db, "oa@example.com", "A", ct).ConfigureAwait(false);
                    User b = await NewUserAsync(db, "ob@example.com", "B", ct).ConfigureAwait(false);
                    Check.True(await RedeemAsync(db, active, a, now, ct).ConfigureAwait(false) && await RedeemAsync(db, active, b, now, ct).ConfigureAwait(false), "Two redemptions");
                    SignupCodeRedemption rb = (await db.Driver.SignupCodeRedemptions.ReadByCodeAndUserAsync(active.Id, b.Id, ct).ConfigureAwait(false))!;
                    rb.RevokedUtc = now;
                    rb.RevokeReason = "test";
                    await db.Driver.SignupCodeRedemptions.UpdateAsync(rb, ct).ConfigureAwait(false);
                    await db.Driver.Subscriptions.CreateAsync(new Subscription { Id = IdGenerator.GenerateSubscriptionId(), UserId = b.Id, Store = StoreEnum.Stripe, StoreSubscriptionId = "sub_conv", ProductId = "p", CreatedUtc = now.AddMinutes(1) }, ct).ConfigureAwait(false);
                    RedemptionOutcomes outcomes = await db.Driver.SignupCodeRedemptions.CountOutcomesAsync(active.Id, now, ct).ConfigureAwait(false);
                    Check.True(outcomes.Active == 1 && outcomes.Revoked == 1 && outcomes.ConvertedToPaid == 1 && outcomes.Lapsed == 0, "Outcomes");
                    Check.Equal(1L, (await db.Driver.SignupCodeRedemptions.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.Redemptions), new RedemptionFilter { SignupCodeId = active.Id, Status = RedemptionStatusEnum.Revoked }, now, ct).ConfigureAwait(false)).TotalRecords, "Status filter");
                    Check.True((await db.Driver.SignupCodeRedemptions.ReadAsync(rb.Id, ct).ConfigureAwait(false))!.ConvertedToPaid, "Converted flag");
                    List<DailyCount> days = await db.Driver.SignupCodeRedemptions.CountByDayAsync(active.Id, now.AddDays(-1), ct).ConfigureAwait(false);
                    Check.True(days.Single().Count == 2 && days.Single().Date == now.ToString("yyyy-MM-dd"), "By day");

                    List<SignupCodeRedemption> due = await db.Driver.SignupCodeRedemptions.EnumerateDueForReminderAsync(now, now.AddMonths(2), 2, 100, ct).ConfigureAwait(false);
                    Check.Equal(1, due.Count, "Only unrevoked redemptions are due");
                    bool[] claims = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => db.Driver.SignupCodeRedemptions.TrySetReminderStageAsync(due[0].Id, 1, ct))).ConfigureAwait(false);
                    Check.Equal(1, claims.Count(c => c), "Reminder claimed once");
                    Check.Equal(0, (await db.Driver.SignupCodeRedemptions.EnumerateDueForReminderAsync(now, now.AddMonths(2), 1, 100, ct).ConfigureAwait(false)).Count, "Stage filter");
                    SignupCodeBatch batch = await db.Driver.SignupCodes.CreateBatchAsync(new SignupCodeBatch { Id = IdGenerator.GenerateSignupCodeBatchId(), Name = "B", Quantity = 2 }, new List<SignupCode>
                    {
                        new SignupCode { Id = IdGenerator.GenerateSignupCodeId(), Code = "BATCH-1", Name = "B", MaxRedemptions = 1 },
                        new SignupCode { Id = IdGenerator.GenerateSignupCodeId(), Code = "BATCH-2", Name = "B", MaxRedemptions = 1 }
                    }, ct).ConfigureAwait(false);
                    Check.Equal(batch.Id, (await db.Driver.SignupCodes.ReadByCodeAsync("BATCH-2", ct).ConfigureAwait(false))!.BatchId, "Batch linked");
                    Check.Equal(6L, (await db.Driver.SignupCodes.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.SignupCodes), new SignupCodeFilter(), now, ct).ConfigureAwait(false)).TotalRecords, "All codes");
                }),
                Case("Audit.Filters", "Audit records page and filter by actor, target, action, and time", async (db, ct) =>
                {
                    DateTime now = DateTime.UtcNow;
                    for (int i = 0; i < 5; i++)
                        await db.Driver.Audit.CreateAsync(new AuditRecord { Id = IdGenerator.GenerateAuditRecordId(), ActorUserId = "usr_a", ActorName = "A", Action = i % 2 == 0 ? "member.ban" : "member.mute", TargetType = "User", TargetId = "usr_t" + i, Summary = "it's " + i, CreatedUtc = now.AddMinutes(-i) }, ct).ConfigureAwait(false);
                    Check.Equal(3L, (await db.Driver.Audit.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.Audit), new AuditFilter { Action = "member.ban" }, ct).ConfigureAwait(false)).TotalRecords, "Action");
                    Check.Equal(1L, (await db.Driver.Audit.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.Audit), new AuditFilter { TargetType = "User", TargetId = "usr_t1" }, ct).ConfigureAwait(false)).TotalRecords, "Target");
                    Check.Equal(2L, (await db.Driver.Audit.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.Audit, new EnumerationQuery { CreatedAfterUtc = now.AddMinutes(-1.5) }), new AuditFilter(), ct).ConfigureAwait(false)).TotalRecords, "From");
                    EnumerationResult<AuditRecord> page = await db.Driver.Audit.EnumerateAsync(TestEnumeration.Staff(PhaseOneCollections.Audit, 2, 2), new AuditFilter { ActorUserId = "usr_a" }, ct).ConfigureAwait(false);
                    Check.True(page.TotalRecords == 5 && page.RecordsRemaining == 1 && page.Objects.Count == 2 && page.Objects[0].Summary == "it's 2", "Paging newest first");
                    Check.Equal(1, (await db.Driver.Audit.EnumerateByUserAsync("usr_t3", 10, ct).ConfigureAwait(false)).Count, "By user");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static async Task<User> NewUserAsync(TestDatabase db, string email, string name, CancellationToken ct)
        {
            return await db.Driver.Users.CreateAsync(new User { Id = IdGenerator.GenerateUserId(), Email = email, DisplayName = name }, ct).ConfigureAwait(false);
        }

        private static Task<Entitlement> GrantAsync(TestDatabase db, string userId, MembershipSourceEnum source, string? sourceId, CancellationToken ct)
        {
            string id = IdGenerator.GenerateEntitlementId();
            return db.Driver.Entitlements.CreateAsync(new Entitlement { Id = id, UserId = userId, Source = source, SourceId = sourceId ?? id, StartsUtc = DateTime.UtcNow.AddMinutes(-1), EndsUtc = DateTime.UtcNow.AddDays(30) }, ct);
        }

        private static async Task<bool> RedeemAsync(TestDatabase db, SignupCode code, User user, DateTime now, CancellationToken ct)
        {
            SignupCodeRedemption redemption = new SignupCodeRedemption { Id = IdGenerator.GenerateSignupCodeRedemptionId(), SignupCodeId = code.Id, UserId = user.Id, RedeemedUtc = now, MembershipStartsUtc = now, MembershipEndsUtc = code.GrantType == GrantTypeEnum.Lifetime ? null : now.AddMonths(1) };
            Entitlement entitlement = new Entitlement { Id = IdGenerator.GenerateEntitlementId(), UserId = user.Id, Source = MembershipSourceEnum.SignupCode, SourceId = redemption.Id, StartsUtc = now, EndsUtc = redemption.MembershipEndsUtc };
            redemption.EntitlementId = entitlement.Id;
            try
            {
                return await db.Driver.SignupCodes.TryRedeemAsync(redemption, entitlement, now, ct).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                return false;
            }
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<TestDatabase, CancellationToken, Task> body)
        {
            return SuiteCases.Database(Id, caseId, name, body);
        }

        #endregion
    }
}
