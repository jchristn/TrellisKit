namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Phase 1 account routes over HTTP: profile updates and validation, handles, avatars, password change, linked
    /// identities, public profiles, export, and account deletion.
    /// </summary>
    public static class ProfileSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Profile";

        #endregion

        #region Private-Members

        private static readonly byte[] _Png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 1, 2, 3, 4 };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Profiles and accounts (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Profile.UpdateAll", "Every profile field updates, rankHistory replaces the list, and profileComplete turns true", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    JsonElement me = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new
                    {
                        displayName = "Pat Example", handle = "Pat.E_1", bio = "Advanced member", rank = "Advanced", rankLevel = 2, affiliation = "Example Club",
                        city = "San Jose", region = "CA", country = "us", goals = new[] { "Learning", "Teaching", "learning" }, goalsOther = (string?)null,
                        rankHistory = new object[] { new { rank = "Advanced", awardedByName = "Coach X", affiliation = "Example Club", dateAwarded = "2015-06-01" }, new { rank = "intermediate" } }
                    }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Pat.E_1", Api.Str(me, "handle"), "Handle");
                    Check.Equal("Advanced", Api.Str(me, "rank"), "Rank");
                    Check.Equal(2, me.GetProperty("rankLevel").GetInt32(), "RankLevel");
                    Check.Equal("US", Api.Str(me, "country"), "Country upper-cased");
                    Check.Equal(2, me.GetProperty("goals").GetArrayLength(), "Goals deduplicated");
                    Check.Equal(2, me.GetProperty("rankHistory").GetArrayLength(), "RankHistory");
                    Check.True(me.GetProperty("profileComplete").GetBoolean(), "Profile complete");
                    string keepId = Api.Str(me.GetProperty("rankHistory")[0], "id");
                    JsonElement updated = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken,
                        new { rankHistory = new object[] { new { id = keepId, rank = "Advanced", awardedByName = "Coach Y" } }, bio = (string?)null }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1, updated.GetProperty("rankHistory").GetArrayLength(), "RankHistory replaced");
                    Check.Equal(keepId, Api.Str(updated.GetProperty("rankHistory")[0], "id"), "Existing id kept");
                    Check.True(updated.GetProperty("bio").ValueKind == JsonValueKind.Null, "Null clears");
                    Check.Equal("Pat Example", Api.Str(updated, "displayName"), "Absent fields unchanged");
                }),
                Case("Profile.Validation", "Invalid fields return camelCase fieldErrors and a taken handle says so", async (f, ct) =>
                {
                    TestMember first = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    TestMember second = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", first.AccessToken, new { handle = "TakenHandle" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    ApiResult taken = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/profile", second.AccessToken, new { handle = "takenhandle" }, ct).ConfigureAwait(false);
                    Check.Equal("That handle is taken.", Api.Str(taken.Body.GetProperty("fieldErrors"), "handle"), "Handle taken");
                    ApiResult bad = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/profile", second.AccessToken, new
                    {
                        displayName = "X", handle = "a b", bio = new string('b', 1001), rank = "Plaid", rankLevel = 7, affiliation = new string('a', 101), country = "ZZ",
                        goals = new[] { "Napping" }, rankHistory = new object[] { new { rank = "", dateAwarded = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd") } }
                    }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, bad.Status, "Status");
                    JsonElement errors = bad.Body.GetProperty("fieldErrors");
                    foreach (string field in new[] { "displayName", "handle", "bio", "rank", "rankLevel", "affiliation", "country", "goals", "rankHistory[0].rank", "rankHistory[0].dateAwarded" })
                        Check.True(errors.TryGetProperty(field, out JsonElement _), "fieldErrors." + field);
                    ApiResult tooMany = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/profile", second.AccessToken, new { rankHistory = Enumerable.Range(0, 13).Select(_ => new { rank = "Beginner" }).ToArray() }, ct).ConfigureAwait(false);
                    Check.True(tooMany.Body.GetProperty("fieldErrors").TryGetProperty("rankHistory", out JsonElement _), "RankHistory limit");
                }),
                Case("Profile.PropertyTaxonomy", "Rank, level, goals, and history are checked against the property: unknown keys and levels above the maximum are rejected", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    int maxLevel = f.Property.Profile.Rank.MaxLevel;
                    string firstRank = f.Property.Profile.Rank.Values[0].Key;
                    ApiResult unknownRank = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { rank = "NotARank" }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, unknownRank.Status, "Unknown rank status");
                    Check.True(Api.Str(unknownRank.Body.GetProperty("fieldErrors"), "rank").Contains(firstRank, StringComparison.Ordinal), "Message lists the property ranks");
                    ApiResult tooHigh = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { rank = firstRank, rankLevel = maxLevel + 1 }, ct).ConfigureAwait(false);
                    Check.Equal("Use 0 to " + maxLevel + ".", Api.Str(tooHigh.Body.GetProperty("fieldErrors"), "rankLevel"), "Level above the maximum");
                    ApiResult unknownGoal = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { goals = new[] { "NotAGoal" } }, ct).ConfigureAwait(false);
                    Check.True(unknownGoal.Body.GetProperty("fieldErrors").TryGetProperty("goals", out JsonElement _), "Unknown goal");
                    ApiResult unknownHistory = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { rankHistory = new object[] { new { rank = "NotARank" } } }, ct).ConfigureAwait(false);
                    Check.True(unknownHistory.Body.GetProperty("fieldErrors").TryGetProperty("rankHistory[0].rank", out JsonElement _), "Unknown history rank");
                    JsonElement ok = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { rank = firstRank.ToLowerInvariant(), rankLevel = maxLevel }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(firstRank, Api.Str(ok, "rank"), "Canonical key stored");
                    Check.Equal(maxLevel, ok.GetProperty("rankLevel").GetInt32(), "Maximum level accepted");
                }),
                SuiteCases.Http(Id, "Profile.DisabledSections", "Values for sections the property disables are rejected, and profileComplete does not need a rank", DisableSections, async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    ApiResult bad = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new
                    {
                        rank = "Beginner", rankLevel = 1, affiliation = "Some Club", goals = new[] { "Learning" }, goalsOther = "text", rankHistory = new object[] { new { rank = "Beginner" } }
                    }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, bad.Status, "Status");
                    JsonElement errors = bad.Body.GetProperty("fieldErrors");
                    foreach (string field in new[] { "rank", "rankLevel", "affiliation", "goals", "goalsOther", "rankHistory" })
                        Check.True(errors.TryGetProperty(field, out JsonElement _), "fieldErrors." + field);
                    JsonElement cleared = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new
                    {
                        handle = "no.rank.needed", rank = (string?)null, rankLevel = 0, affiliation = (string?)null, goals = new string[0], rankHistory = new object[0]
                    }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(cleared.GetProperty("profileComplete").GetBoolean(), "Complete without a rank when ranks are disabled");
                    JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!property.GetProperty("profile").GetProperty("rank").GetProperty("enabled").GetBoolean(), "Property route reports the disabled section");
                }),
                Case("Regression.Profile.NulEcho", "A profile update with NUL characters echoes exactly what is stored (NUL removed)", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    JsonElement updated = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, "{\"bio\":\"Hello\\u0000World\",\"city\":\"Spring\\u0000field\"}", HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("HelloWorld", Api.Str(updated, "bio"), "Echoed bio");
                    Check.Equal("Springfield", Api.Str(updated, "city"), "Echoed city");
                    JsonElement me = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(Api.Str(updated, "bio"), Api.Str(me, "bio"), "Stored bio equals the echo");
                }),
                Case("Regression.Profile.MutedIdentity", "A muted member cannot change avatar, bio, or display name: 403 Muted with mutedUntilUtc in the body and a plain message; other fields and unchanged values pass; unmuting restores it", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { bio = "before", displayName = "Muted Person" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    DateTime until = DateTime.UtcNow.AddDays(2);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + member.Id + "/mute", HttpFixture.AdminToken, new { untilUtc = until, reason = "cool off" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    foreach (object body in new object[] { new { bio = "after" }, new { displayName = "Renamed Person" } })
                    {
                        ApiResult result = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, body, ct).ConfigureAwait(false);
                        Check.Equal(HttpStatusCode.Forbidden, result.Status, "Muted status (body: " + result.Text + ")");
                        Check.Equal("Muted", result.Error, "Muted error");
                        Check.True(Math.Abs((result.Body.GetProperty("mutedUntilUtc").GetDateTime().ToUniversalTime() - until).TotalSeconds) < 2, "mutedUntilUtc in the body");
                        Check.True(!System.Text.RegularExpressions.Regex.IsMatch(Api.Str(result.Body, "message"), "\\d{4}-\\d{2}-\\d{2}"), "Message has no timestamp: " + Api.Str(result.Body, "message"));
                    }
                    ApiResult avatar = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/avatar", member.AccessToken, null, ct, "mobile", null,
                        new ByteArrayContent(_Png) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }).ConfigureAwait(false);
                    Check.Equal("Muted", avatar.Error, "Avatar upload muted (body: " + avatar.Text + ")");
                    Check.True(avatar.Body.TryGetProperty("mutedUntilUtc", out JsonElement _), "Avatar error carries mutedUntilUtc");
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/me/avatar", member.AccessToken, null, HttpStatusCode.Forbidden, "Muted", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { city = "Elsewhere", bio = "before", displayName = "Muted Person" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + member.Id + "/unmute", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { bio = "after" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await UploadAsync(f, member, _Png, "image/png", HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),
                Case("Regression.API07.WrongJsonTypes", "Well-formed JSON with a wrong value type is 400 ValidationFailed naming the field; malformed JSON stays InvalidJson", async (f, ct) =>
                {
                    TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    async Task ExpectField(HttpMethod method, string path, string? bearer, string body, string field)
                    {
                        ApiResult result = await Api.SendAsync(f, method, path, bearer, body, ct).ConfigureAwait(false);
                        Check.Equal(HttpStatusCode.BadRequest, result.Status, path + " status (body: " + result.Text + ")");
                        Check.Equal("ValidationFailed", result.Error, path + " error");
                        Check.True(result.Body.GetProperty("fieldErrors").TryGetProperty(field, out JsonElement _), path + " names " + field + " (body: " + result.Text + ")");
                    }
                    await ExpectField(HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, "{\"rankLevel\":\"two\"}", "rankLevel").ConfigureAwait(false);
                    await ExpectField(HttpMethod.Put, "/api/v1/admin/members/" + member.Id + "/flags", HttpFixture.AdminToken, "{\"canPost\":\"yes\"}", "canPost").ConfigureAwait(false);
                    await ExpectField(HttpMethod.Post, "/api/v1/admin/moderation/word-filter", HttpFixture.AdminToken, "{\"term\":\"zzq\",\"action\":\"Nuke\"}", "action").ConfigureAwait(false);
                    await ExpectField(HttpMethod.Post, "/api/v1/admin/signup-codes/enumerate", HttpFixture.AdminToken, "{\"maxResults\":\"10\"}", "maxResults").ConfigureAwait(false);
                    ApiResult enumMessage = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter", HttpFixture.AdminToken, "{\"term\":\"zzq\",\"action\":\"Nuke\"}", ct).ConfigureAwait(false);
                    Check.True(enumMessage.Text.Contains("Block", StringComparison.Ordinal), "Enum fields list the allowed values");
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, "{\"rankLevel\":", HttpStatusCode.BadRequest, "InvalidJson", ct).ConfigureAwait(false);
                }),
                Case("Handles.Availability", "Handle availability counts the caller's own handle as available and rejects invalid handles", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    TestMember other = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { handle = "mine.handle" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement own = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/handles/MINE.HANDLE/availability", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(own.GetProperty("available").GetBoolean(), "Own handle available");
                    JsonElement theirs = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/handles/mine.handle/availability", other.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(!theirs.GetProperty("available").GetBoolean(), "Taken for others");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/handles/x/availability", other.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                }),
                Case("Avatar.UploadServeDelete", "Avatars are validated by magic bytes, size limited, served with the right type, and deleted", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    JsonElement me = await UploadAsync(f, member, _Png, "image/png", HttpStatusCode.OK, ct).ConfigureAwait(false);
                    string url = Api.Str(me, "avatarUrl");
                    Check.True(url.StartsWith("/api/v1/media/avatars/" + member.Id + "_", StringComparison.Ordinal) && url.EndsWith(".png", StringComparison.Ordinal), "Avatar URL");
                    using (HttpResponseMessage served = await f.Client.GetAsync(url, ct).ConfigureAwait(false))
                    {
                        Check.Equal(HttpStatusCode.OK, served.StatusCode, "Served anonymously");
                        Check.Equal("image/png", served.Content.Headers.ContentType?.MediaType, "Content type");
                        Check.Equal(_Png.Length, (await served.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false)).Length, "Bytes");
                    }
                    ApiResult withToken = await Api.SendAsync(f, HttpMethod.Get, url, member.AccessToken, null, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, withToken.Status, "Served with a bearer token too");
                    await UploadAsync(f, member, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 }, "image/png", HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    await UploadAsync(f, member, _Png, "image/jpeg", HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    byte[] big = new byte[f.Settings.Storage.MaxAvatarBytes + 1];
                    _Png.CopyTo(big, 0);
                    await UploadAsync(f, member, big, "image/png", (HttpStatusCode)413, ct).ConfigureAwait(false);
                    JsonElement deleted = await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me/avatar", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(deleted.GetProperty("avatarUrl").ValueKind == JsonValueKind.Null, "Avatar removed");
                    using (HttpResponseMessage gone = await f.Client.GetAsync(url, ct).ConfigureAwait(false))
                        Check.Equal(HttpStatusCode.NotFound, gone.StatusCode, "File deleted");
                    using (HttpResponseMessage traversal = await f.Client.GetAsync("/api/v1/media/avatars/..%2F..%2Fsecret.png", ct).ConfigureAwait(false))
                        Check.Equal(HttpStatusCode.NotFound, traversal.StatusCode, "Traversal rejected");
                }),
                Case("Password.Change", "Changing the password needs the current one and revokes other sessions", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    TestMember otherSession = await Api.LoginAsync(f, new TestMember { Id = member.Id, Email = member.Email, Password = member.Password }, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/password", member.AccessToken, new { newPassword = "another passphrase" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/password", member.AccessToken, new { currentPassword = "wrong one here", newPassword = "another passphrase" }, HttpStatusCode.Unauthorized, "InvalidCredentials", ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/password", member.AccessToken, new { currentPassword = member.Password, newPassword = "another passphrase" }, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/auth/me", otherSession.AccessToken, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    member.Password = "another passphrase";
                    await Api.LoginAsync(f, member, ct).ConfigureAwait(false);
                }),
                Case("Identities.LinkUnlink", "Linking, IdentityInUse, and LastSignInMethod", async (f, ct) =>
                {
                    TestMember social = await Api.SocialAsync(f, ct, "google").ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/me/identities/google", social.AccessToken, null, HttpStatusCode.Conflict, "LastSignInMethod", ct).ConfigureAwait(false);
                    string apple = f.Fakes.Apple.Script("apple-link", "apple-sub-link", null);
                    JsonElement linked = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/identities/apple", social.AccessToken, new { identityToken = apple, nonce = "n" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(2, linked.GetProperty("signInMethods").GetArrayLength(), "Two methods");
                    TestMember other = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/me/identities/apple", other.AccessToken, new { identityToken = apple, nonce = "n" }, HttpStatusCode.Conflict, "IdentityInUse", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/me/identities/apple", other.AccessToken, new { identityToken = "bogus" }, HttpStatusCode.Unauthorized, "InvalidIdentityToken", ct).ConfigureAwait(false);
                    JsonElement unlinked = await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me/identities/google", social.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Apple", unlinked.GetProperty("signInMethods")[0].GetString(), "Google removed");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/me/identities/myspace", social.AccessToken, new { }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                }),
                Case("Users.PublicProfile", "Public profiles expose only public fields and hide deleted and banned members", async (f, ct) =>
                {
                    TestMember viewer = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                    TestMember subject = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", subject.AccessToken, new { handle = "subject1", rank = "Intermediate" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement profile = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + subject.Id, viewer.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("subject1", Api.Str(profile, "handle"), "Handle");
                    Check.True(!profile.TryGetProperty("email", out JsonElement _), "No email");
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + subject.Id + "/ban", HttpFixture.AdminToken, new { reason = "spam" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/users/" + subject.Id, viewer.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
                }),
                Case("Account.ExportAndDelete", "Export returns everything; deletion anonymizes, revokes sessions, and cancels Stripe at period end", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new { handle = "goner", bio = "bye" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await UploadAsync(f, member, _Png, "image/png", HttpStatusCode.OK, ct).ConfigureAwait(false);
                    f.Fakes.Stripe.Put(new StripeSubscription { Id = "sub_stripe_del", CustomerId = "cus_del", Status = "active", UserId = member.Id, CurrentPeriodStartUtc = DateTime.UtcNow.AddDays(-1), CurrentPeriodEndUtc = DateTime.UtcNow.AddDays(364) });
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/webhooks/stripe", null, "{\"id\":\"evt_del\",\"type\":\"customer.subscription.created\",\"subscriptionId\":\"sub_stripe_del\",\"customerId\":\"cus_del\"}", HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    ApiResult hook = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/webhooks/stripe", null, "{\"id\":\"evt_del\",\"type\":\"customer.subscription.created\",\"subscriptionId\":\"sub_stripe_del\",\"customerId\":\"cus_del\"}", ct,
                        "mobile", new Dictionary<string, string> { { "Stripe-Signature", "valid" } }).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.OK, hook.Status, "Webhook (body: " + hook.Text + ")");

                    JsonElement export = await AccountExportSuite.ExportAsync(f, member, ct).ConfigureAwait(false);
                    Check.Equal(member.Email, Api.Str(export.GetProperty("profile"), "email"), "Profile exported");
                    Check.Equal(1, export.GetProperty("subscriptions").GetArrayLength(), "Subscriptions exported");
                    Check.True(export.GetProperty("sessions").GetArrayLength() >= 1, "Sessions exported");

                    await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/me", member.AccessToken, new { confirm = "yes" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                    JsonElement deleted = await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", member.AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    Check.True(deleted.GetProperty("storeSubscriptionNote").ValueKind == JsonValueKind.Null, "No store note for Stripe");
                    Check.True(f.Fakes.Stripe.Cancelled.Contains("sub_stripe_del"), "Stripe cancelled at period end");
                    await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/auth/login", null, new { email = member.Email, password = member.Password }, HttpStatusCode.Unauthorized, "InvalidCredentials", ct).ConfigureAwait(false);
                    TrellisKit.Core.Models.User? user = await f.Database.Users.ReadAsync(member.Id, ct).ConfigureAwait(false);
                    Check.True(user!.IsDeleted && user.Email == null && user.Handle == null && user.Bio == null && user.AvatarFile == null, "Anonymized");
                    await Api.RegisterAsync(f, ct, member.Email).ConfigureAwait(false);
                    JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?action=account.delete", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(1L, audit.GetProperty("totalRecords").GetInt64(), "Audited");
                }),
                Case("Account.DeleteWithAppleNote", "Deleting with an active Apple subscription tells the member to cancel it in Settings", async (f, ct) =>
                {
                    TestMember member = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
                    TrellisKit.Core.Models.User? user = await f.Database.Users.ReadAsync(member.Id, ct).ConfigureAwait(false);
                    f.Fakes.AppStore.PutTransaction("jws-del", new AppStoreTransaction { TransactionId = "t1", OriginalTransactionId = "o-del", ProductId = f.Services.Property.AppStoreProductId, PurchaseUtc = DateTime.UtcNow, ExpiresUtc = DateTime.UtcNow.AddDays(30), AppAccountToken = Guid.Parse(user!.StoreAccountToken) });
                    await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/membership/apple/verify", member.AccessToken, new { signedTransaction = "jws-del" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement deleted = await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", member.AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    Check.Equal("Cancel your App Store subscription in iPhone Settings.", Api.Str(deleted, "storeSubscriptionNote"), "Apple note");
                })
            });
        }

        #endregion

        #region Private-Methods

        private static async Task<JsonElement> UploadAsync(HttpFixture f, TestMember member, byte[] data, string contentType, HttpStatusCode expected, CancellationToken ct)
        {
            ByteArrayContent content = new ByteArrayContent(data);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            ApiResult result = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/me/avatar", member.AccessToken, null, ct, "mobile", null, content).ConfigureAwait(false);
            Check.Equal(expected, result.Status, "Avatar upload " + contentType + " (" + data.Length + " bytes, body: " + result.Text + ")");
            return result.Body;
        }

        private static void DisableSections(TrellisKit.Core.PropertyConfig.PropertyConfiguration property)
        {
            property.Profile.Rank.Enabled = false;
            property.Profile.RankHistory.Enabled = false;
            property.Profile.Affiliation.Enabled = false;
            property.Profile.Goals.Enabled = false;
            foreach (TrellisKit.Core.Models.ProfileField field in property.ProfileFields ?? new List<TrellisKit.Core.Models.ProfileField>())
                if (field.Key == "rank" || field.Key == "rankHistory" || field.Key == "affiliation" || field.Key == "goals") field.Enabled = false;
        }

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        #endregion
    }
}
