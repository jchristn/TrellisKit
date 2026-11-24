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

    /// <summary>
    /// Phase 4 directory access (docs/PHASE4_CONTRACT.md sections 0.4, 1, and 3.11; resolved decision 2): every access
    /// model, PartiallyGated locked stubs for venues, audiences, the DirectoryModerator role, public routes, the kill
    /// switch, and the feature switch.
    /// </summary>
    public static class DirectoryAccessSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "DirectoryAccess";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Directory access (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Access.FullyGated", "FullyGated: members read and list; free accounts get 402; visitors 401; new entries default to Members", FullyGatedAsync),
                Case("Access.PartiallyGatedStubs", "PartiallyGated with the directory gated: free accounts list Members venues as locked stubs without details, maps and searches show no stubs, submissions need a membership; members read in full", StubsAsync),
                Case("Access.OpenAndAnonymous", "Open and PartiallyGated not gated: signed-in accounts read SignedIn entries, visitors browse only Public entries under anonymous browsing; Public needs a SystemAdmin", OpenAsync),
                Case("Access.Roles", "DirectoryModerator manages venues, instructors, and the review queue but not programs or settings; members cannot reach admin routes", RolesAsync),
                Case("Access.PublicRoutes", "Public routes answer 404 NotAvailable until directory.publicListingsEnabled, then only Public, Active venues without email or viewer, cached for 300 seconds", PublicAsync),
                Case("Access.KillSwitch", "The directory kill switch answers 503 FeatureDisabled on member directory routes while admin routes stay open", KillAsync),
                SuiteCases.Http(Id, "Access.FeatureOff", "With features.directory false every venue, instructor, directory, admin, and public directory route is 404 NotAvailable", p => { p.Features.Directory = false; }, FeatureOffAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                DirectoryTestKit.Relax(f);
                return body(f, ct);
            });
        }

        private static Task<JsonElement> AccessAsync(HttpFixture f, object body, CancellationToken ct)
        {
            return Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, body, HttpStatusCode.OK, ct);
        }

        private static async Task FullyGatedAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            JsonElement venue = await DirectoryTestKit.VenueAsync(f, "Gated Club", ct).ConfigureAwait(false);
            Check.Equal("Members", Api.Str(venue, "audience"), "Default audience");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/" + Api.Str(venue, "id"), member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult freeRead = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/venues/enumerate", free.AccessToken, new { }, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)402, freeRead.Status, "Free accounts need a membership");
            ApiResult visitor = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/venues/enumerate", null, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Unauthorized, visitor.Status, "Visitors sign in");
            ApiResult freeSubmit = await Api.SendAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, free.AccessToken, new { type = "NewVenue", fields = new { name = "Free Club" } }, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)402, freeSubmit.Status, "Submissions need a membership");
        }

        private static async Task StubsAsync(HttpFixture f, CancellationToken ct)
        {
            await AccessAsync(f, new { model = "PartiallyGated", anonymousBrowsing = true, gatedFeatures = new[] { "directory" }, defaultAudience = "SignedIn" }, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            JsonElement gated = await DirectoryTestKit.VenueAsync(f, "Members Club", ct, 10.0, 10.0, new
            {
                email = "secret@example.test",
                summary = "Members only details",
                address = new { line1 = "1 Hidden Street", locality = "Exampleville", countryCode = "US" }
            }).ConfigureAwait(false);
            Check.Equal("Members", Api.Str(gated, "audience"), "Gated feature defaults to Members");
            string gatedId = Api.Str(gated, "id");
            string open = await DirectoryTestKit.VenueIdAsync(f, "Open Club", ct, 10.1, 10.1, new { audience = "SignedIn" }).ConfigureAwait(false);
            JsonElement list = await DirectoryTestKit.ListAsync(f, free.AccessToken, new { }, ct).ConfigureAwait(false);
            Check.Equal(2, list.GetProperty("objects").GetArrayLength(), "Free accounts list both");
            JsonElement stub = DirectoryTestKit.Find(list, gatedId);
            Check.True(stub.GetProperty("locked").GetBoolean(), "Locked stub");
            Check.True(!stub.GetProperty("viewer").GetProperty("canOpen").GetBoolean() && Api.Str(stub.GetProperty("viewer"), "requires") == "Membership", "Stub viewer");
            Check.True(stub.GetProperty("summary").ValueKind == JsonValueKind.Null && stub.GetProperty("email").ValueKind == JsonValueKind.Null && stub.GetProperty("geo").GetProperty("lat").ValueKind == JsonValueKind.Null, "No details in a stub");
            Check.True(!stub.ToString().Contains("Hidden Street", StringComparison.Ordinal), "No street address in a stub");
            Check.True(!DirectoryTestKit.Find(list, open).GetProperty("locked").GetBoolean(), "SignedIn entries open");
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/" + gatedId, free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(detail.GetProperty("locked").GetBoolean() && detail.GetProperty("description").ValueKind == JsonValueKind.Null, "Detail is a stub too");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/venues/" + gatedId + "/instructors/enumerate", free.AccessToken, new { }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement map = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/directory/map/enumerate", free.AccessToken, new { bbox = new { south = 9, west = 9, north = 11, east = 11 }, zoom = 8 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, map.GetProperty("objects").GetArrayLength(), "Maps show only open entries");
            JsonElement full = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/" + gatedId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!full.GetProperty("locked").GetBoolean() && Api.Str(full, "summary") == "Members only details", "Members read in full");
            ApiResult freeSubmit = await Api.SendAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, free.AccessToken, new { type = "NewVenue", fields = new { name = "Free Club" } }, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)402, freeSubmit.Status, "A gated directory needs a membership to submit");
            ApiResult visitor = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/venues/enumerate", null, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, visitor.Status, "Visitors may browse");
            Check.Equal(0, visitor.Body.GetProperty("objects").GetArrayLength(), "Visitors see no stubs and no SignedIn entries");
            JsonElement coach = await DirectoryTestKit.InstructorAsync(f, "Members Coach", ct).ConfigureAwait(false);
            JsonElement coaches = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/instructors/enumerate", free.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(DirectoryTestKit.Find(coaches, Api.Str(coach, "id")).GetProperty("locked").GetBoolean(), "Instructor stubs");
        }

        private static async Task OpenAsync(HttpFixture f, CancellationToken ct)
        {
            await AccessAsync(f, new { model = "Open", anonymousBrowsing = true }, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember moderator = await DirectoryTestKit.ModeratorAsync(f, ct, false).ConfigureAwait(false);
            JsonElement signedIn = await DirectoryTestKit.VenueAsync(f, "Signed In Club", ct).ConfigureAwait(false);
            Check.Equal("SignedIn", Api.Str(signedIn, "audience"), "Open defaults to SignedIn");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues, moderator.AccessToken, new { name = "Public Attempt", audience = "Public" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement pub = await DirectoryTestKit.VenueAsync(f, "Public Club", ct, extra: new { audience = "Public" }, bearer: admin.AccessToken).ConfigureAwait(false);
            JsonElement freeList = await DirectoryTestKit.ListAsync(f, free.AccessToken, new { }, ct).ConfigureAwait(false);
            Check.Equal(2, freeList.GetProperty("objects").GetArrayLength(), "Signed-in accounts read both");
            ApiResult visitor = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/venues/enumerate", null, new { }, ct).ConfigureAwait(false);
            Check.True(DirectoryTestKit.Ids(visitor.Body).SequenceEqual(new List<string> { Api.Str(pub, "id") }), "Visitors browse Public entries only");
            Check.True(!visitor.Body.GetProperty("objects")[0].GetProperty("viewer").GetProperty("canSuggestEdit").GetBoolean(), "Visitors cannot take part");
            ApiResult hidden = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/venues/" + Api.Str(signedIn, "id"), null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.NotFound, hidden.Status, "SignedIn entries are not found for visitors");
            JsonElement freeSubmit = await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, free.AccessToken, new { type = "NewVenue", fields = new { name = "Open Suggestion" } }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.Equal("Pending", Api.Str(freeSubmit, "status"), "Signed-in accounts submit in Open");
        }

        private static async Task RolesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember moderator = await DirectoryTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            JsonElement venue = await DirectoryTestKit.VenueAsync(f, "Moderated Club", ct, bearer: moderator.AccessToken).ConfigureAwait(false);
            Check.True(venue.GetProperty("viewer").GetProperty("canEdit").GetBoolean(), "Moderator viewer");
            await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.AdminInstructors, moderator.AccessToken, new { displayName = "Moderated Coach" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.Review + "/enumerate", moderator.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, DirectoryTestKit.Settings, moderator.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/directory/programs/order", moderator.AccessToken, new { programIds = new string[0] }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            foreach (string path in new[] { DirectoryTestKit.AdminVenues + "/enumerate", DirectoryTestKit.AdminInstructors + "/enumerate", DirectoryTestKit.Review + "/enumerate" })
                await Api.ExpectErrorAsync(f, HttpMethod.Post, path, member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/directory/metrics", member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/venues/ven_doesnotexist", member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task PublicAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            JsonElement pub = await DirectoryTestKit.VenueAsync(f, "Public Listing", ct, 30.0, 30.0, new { audience = "Public", email = "public@example.test", phone = "+1 555 0100" }, admin.AccessToken).ConfigureAwait(false);
            await DirectoryTestKit.VenueAsync(f, "Members Listing", ct, 30.1, 30.1).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/public/venues/enumerate", null, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await DirectoryTestKit.SettingsAsync(f, new { values = new Dictionary<string, object> { { "directory.publicListingsEnabled", true } } }, ct).ConfigureAwait(false);
            ApiResult list = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/public/venues/enumerate", null, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, list.Status, "Public list");
            Check.True(DirectoryTestKit.Ids(list.Body).SequenceEqual(new List<string> { Api.Str(pub, "id") }), "Only Public venues");
            JsonElement item = list.Body.GetProperty("objects")[0];
            Check.True(item.GetProperty("email").ValueKind == JsonValueKind.Null && item.GetProperty("phone").ValueKind == JsonValueKind.Null && item.GetProperty("viewer").ValueKind == JsonValueKind.Null, "No contact details or viewer");
            Check.Equal("public, max-age=300", list.Headers.TryGetValue("Cache-Control", out string? cache) ? cache : null, "Cached");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/venues/" + Api.Str(pub, "id"), null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement map = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/public/directory/map/enumerate", null, new { bbox = new { south = 29, west = 29, north = 31, east = 31 }, zoom = 8 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, map.GetProperty("objects").GetArrayLength(), "Public map");
            await DirectoryTestKit.SettingsAsync(f, new { values = new Dictionary<string, object> { { "directory.publicShowsContact", true } } }, ct).ConfigureAwait(false);
            JsonElement contact = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/venues/" + Api.Str(pub, "id"), null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("+1 555 0100", Api.Str(contact, "phone"), "Phone with publicShowsContact");
            Check.True(contact.GetProperty("email").ValueKind == JsonValueKind.Null, "Email is never public");
            await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues + "/" + Api.Str(pub, "id") + "/close", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/venues/" + Api.Str(pub, "id"), null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task KillAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/venues/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/app/kill-switches/directory", HttpFixture.AdminToken, new { killed = true, reason = "Incident" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult blocked = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/venues/enumerate", member.AccessToken, new { }, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)503, blocked.Status, "Blocked");
            Check.Equal("FeatureDisabled", blocked.Error, "FeatureDisabled");
            ApiResult submit = await Api.SendAsync(f, HttpMethod.Post, DirectoryTestKit.Submissions, member.AccessToken, new { type = "NewVenue", fields = new { name = "Killed Club" } }, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)503, submit.Status, "Submissions blocked");
            await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues + "/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/app/kill-switches/directory", HttpFixture.AdminToken, new { killed = false, reason = "Resolved" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task FeatureOffAsync(HttpFixture f, CancellationToken ct)
        {
            DirectoryTestKit.Relax(f);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            foreach (string path in new[] { "/api/v1/venues/enumerate", "/api/v1/instructors/enumerate", "/api/v1/directory/map/enumerate", "/api/v1/directory/submissions", "/api/v1/me/directory/submissions/enumerate" })
            {
                ApiResult off = await Api.SendAsync(f, HttpMethod.Post, path, member.AccessToken, new { }, ct).ConfigureAwait(false);
                Check.Equal(HttpStatusCode.NotFound, off.Status, path + " is off");
                Check.Equal("NotAvailable", off.Error, path + " NotAvailable");
            }
            ApiResult admin = await Api.SendAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues + "/enumerate", HttpFixture.AdminToken, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.NotFound, admin.Status, "Admin routes are off too");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/settings", HttpFixture.AdminToken, new { values = new Dictionary<string, object> { { "directory.publicListingsEnabled", true } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult pub = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/public/venues/enumerate", null, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.NotFound, pub.Status, "Public routes are off");
        }

        #endregion
    }
}
