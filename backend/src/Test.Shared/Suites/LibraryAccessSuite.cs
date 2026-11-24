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
    /// The library under every access model (docs/PHASE4_CONTRACT.md section 0.4): PartiallyGated with the library
    /// gated (locked stubs, samples that need only an account, anonymous Public lessons) and not gated, Open,
    /// FullyGated with public sample routes, the feature switch (404 NotAvailable), and the library kill switch.
    /// </summary>
    public static class LibraryAccessSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "LibraryAccess";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Library access models (" + TestEnvironment.Provider + ")", new[]
            {
                Case("PartiallyGated.Gated", "Library gated: free accounts see Members series and lessons as locked stubs with requires Membership, open sample lessons (SignedIn or Public) and take part on them, and get 402 on Members progress; visitors see only Public lessons and get 401 on progress", GatedAsync),
                Case("PartiallyGated.NotGated", "Library not gated: new series take the default audience, free accounts read SignedIn lessons and still see Members lessons as stubs", NotGatedAsync),
                Case("Open.EveryAccount", "Open: Members lessons are SignedIn content, so every account reads and plays them", OpenAsync),
                Case("FullyGated.PublicSamples", "FullyGated: free accounts get 402 and visitors 401 on library routes; the public sample routes serve Public lessons only, and stop when public samples are off", FullyGatedAsync),
                Case("KillSwitch.Library", "The library kill switch answers 503 FeatureDisabled on member library routes while staff routes stay open", KillSwitchAsync),
                SuiteCases.Http(Id, "FeatureOff.NotAvailable", "features.library false: member, public, and admin library routes answer 404 NotAvailable", p => p.Features.Library = false, FeatureOffAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                LibraryTestKit.Relax(f);
                return body(f, ct);
            });
        }

        private static async Task<Dictionary<string, string>> WorldAsync(HttpFixture f, TestMember admin, CancellationToken ct)
        {
            string series = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Members course", ct, new { audience = "Members" }).ConfigureAwait(false), "id");
            Dictionary<string, string> w = new Dictionary<string, string> { { "series", series } };
            w["members"] = await LibraryTestKit.VideoLessonAsync(f, admin, series, "Members lesson", 100, ct, new { audience = "Members" }).ConfigureAwait(false);
            w["sample"] = await LibraryTestKit.VideoLessonAsync(f, admin, series, "Sample lesson", 100, ct, new { audience = "SignedIn" }).ConfigureAwait(false);
            w["public"] = await LibraryTestKit.VideoLessonAsync(f, admin, series, "Public lesson", 100, ct, new { audience = "Public" }).ConfigureAwait(false);
            return w;
        }

        private static async Task GatedAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            await LibraryTestKit.SetAccessAsync(f, new { model = "PartiallyGated", anonymousBrowsing = true, gatedFeatures = new[] { "library" }, defaultAudience = "SignedIn" }, ct).ConfigureAwait(false);
            Dictionary<string, string> w = await WorldAsync(f, admin, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            JsonElement seriesList = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/series/enumerate", free.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement stub = LibraryTestKit.Find(seriesList, w["series"]);
            Check.True(stub.GetProperty("locked").GetBoolean(), "Members series is a locked stub");
            Check.Equal("Membership", Api.Str(stub.GetProperty("viewer"), "requires"), "requires Membership");
            JsonElement lessons = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/series/" + w["series"] + "/lessons/enumerate", free.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> listed = LibraryTestKit.Ids(lessons);
            Check.True(listed.Contains(w["sample"]) && listed.Contains(w["public"]) && !listed.Contains(w["members"]), "A locked series lists only the lessons the caller may open");
            JsonElement all = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/enumerate", free.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement memberStub = LibraryTestKit.Find(all, w["members"]);
            Check.True(memberStub.GetProperty("locked").GetBoolean() && memberStub.GetProperty("video").ValueKind == JsonValueKind.Null, "Members lesson is a stub in lists");
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + w["members"], free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(detail.GetProperty("locked").GetBoolean() && detail.GetProperty("description").ValueKind == JsonValueKind.Null && detail.GetProperty("video").ValueKind == JsonValueKind.Null, "Stub detail");
            Check.True(!detail.GetProperty("viewer").GetProperty("canPlay").GetBoolean(), "Stub cannot play");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/lessons/" + w["members"] + "/progress", free.AccessToken, new { positionSeconds = 10, playedSeconds = 5 }, (HttpStatusCode)402, "MembershipRequired", ct).ConfigureAwait(false);
            JsonElement sample = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + w["sample"], free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!sample.GetProperty("locked").GetBoolean() && sample.GetProperty("viewer").GetProperty("canPlay").GetBoolean(), "Sample lessons play for free accounts");
            await LibraryTestKit.ProgressAsync(f, free.AccessToken, w["sample"], 10, 5, DateTime.UtcNow, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/" + w["sample"] + "/comments", free.AccessToken, new { body = "Free comment on a sample" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            ApiResult memberComment = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/lessons/" + w["members"] + "/comments", free.AccessToken, new { body = "Not allowed" }, ct).ConfigureAwait(false);
            Check.True(memberComment.Status == (HttpStatusCode)402 || memberComment.Status == HttpStatusCode.NotFound, "No comments on locked lessons (" + (int)memberComment.Status + ")");
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement full = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + w["members"], member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!full.GetProperty("locked").GetBoolean(), "Members read in full");
            JsonElement anonymous = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/enumerate", null, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(LibraryTestKit.Ids(anonymous).SequenceEqual(new[] { w["public"] }), "Visitors see only Public lessons, no stubs");
            JsonElement guest = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + w["public"], null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(guest.GetProperty("viewer").GetProperty("canPlay").GetBoolean(), "A guest may play a Public lesson");
            Check.True(Api.Str(guest.GetProperty("video").GetProperty("video"), "hlsUrl").Contains("/public/", StringComparison.Ordinal), "A guest plays a Public lesson from the public paths (no media grant)");
            Check.True(!guest.GetProperty("video").TryGetProperty("ownerUserId", out JsonElement _), "Guests never see the uploader");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/lessons/" + w["members"], null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            ApiResult anonProgress = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/lessons/" + w["public"] + "/progress", null, new { positionSeconds = 1 }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Unauthorized, anonProgress.Status, "Visitors cannot record progress");
        }

        private static async Task NotGatedAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            await LibraryTestKit.SetAccessAsync(f, new { model = "PartiallyGated", anonymousBrowsing = true, gatedFeatures = Array.Empty<string>(), defaultAudience = "SignedIn" }, ct).ConfigureAwait(false);
            JsonElement series = await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Open course", ct).ConfigureAwait(false);
            Check.Equal("SignedIn", Api.Str(series, "audience"), "New series take the default audience");
            string signedIn = await LibraryTestKit.VideoLessonAsync(f, admin, Api.Str(series, "id"), "Free lesson", 100, ct).ConfigureAwait(false);
            string members = await LibraryTestKit.VideoLessonAsync(f, admin, Api.Str(series, "id"), "Paid lesson", 100, ct, new { audience = "Members" }).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            JsonElement a = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + signedIn, free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!a.GetProperty("locked").GetBoolean(), "SignedIn lessons open");
            await LibraryTestKit.ProgressAsync(f, free.AccessToken, signedIn, 5, 5, DateTime.UtcNow, ct).ConfigureAwait(false);
            JsonElement b = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + members, free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(b.GetProperty("locked").GetBoolean(), "Members lessons are stubs");
        }

        private static async Task OpenAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            Dictionary<string, string> w = await WorldAsync(f, admin, ct).ConfigureAwait(false);
            await LibraryTestKit.SetAccessAsync(f, new { model = "Open", anonymousBrowsing = false }, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + w["members"], free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!detail.GetProperty("locked").GetBoolean() && detail.GetProperty("viewer").GetProperty("canPlay").GetBoolean(), "Every account reads Members lessons in Open");
            await LibraryTestKit.ProgressAsync(f, free.AccessToken, w["members"], 5, 5, DateTime.UtcNow, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/series", null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
        }

        private static async Task FullyGatedAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            Dictionary<string, string> w = await WorldAsync(f, admin, ct).ConfigureAwait(false);
            await LibraryTestKit.SetAccessAsync(f, new { model = "FullyGated" }, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            ApiResult denied = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/series/enumerate", free.AccessToken, new { }, ct).ConfigureAwait(false);
            Check.Equal((HttpStatusCode)402, denied.Status, "Free accounts need a membership");
            ApiResult visitor = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/lessons/enumerate", null, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Unauthorized, visitor.Status, "Visitors sign in");
            JsonElement samples = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/public/lessons/enumerate", null, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(LibraryTestKit.Ids(samples).SequenceEqual(new[] { w["public"] }), "Public samples only");
            JsonElement one = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/lessons/" + w["public"], null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(one.TryGetProperty("description", out JsonElement _), "Public lesson carries its description");
            foreach (string omitted in new[] { "viewer", "myProgress", "myReactions", "commentTarget", "notifyStaffOnComment", "moderationState", "mediaStatus", "resources", "previous", "next", "promotion", "publishAtUtc", "status", "locked" })
                Check.True(!one.TryGetProperty(omitted, out JsonElement _), "PublicLesson omits " + omitted);
            JsonElement publicVideo = one.GetProperty("video");
            Check.True(publicVideo.ValueKind == JsonValueKind.Object && !publicVideo.TryGetProperty("ownerUserId", out JsonElement _), "The public video never names the uploading staff member");
            Check.True(Api.Str(publicVideo.GetProperty("video"), "hlsUrl").Contains("/public/", StringComparison.Ordinal), "The public video plays from the public paths");
            JsonElement listedSample = samples.GetProperty("objects")[0];
            Check.True(!listedSample.TryGetProperty("viewer", out JsonElement _) && !listedSample.GetProperty("video").TryGetProperty("ownerUserId", out JsonElement _), "Listed samples are trimmed the same way");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/lessons/" + w["members"], null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/library/settings", admin.AccessToken, new { publicSamplesEnabled = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/public/lessons/enumerate", null, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + w["members"], member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task KillSwitchAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Paused course", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/app/kill-switches/library", HttpFixture.AdminToken, new { killed = true, message = "The library is paused.", reason = "Test" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult killed = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/series/enumerate", member.AccessToken, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.ServiceUnavailable, killed.Status, "503 under the kill switch");
            Check.Equal("FeatureDisabled", killed.Error, "FeatureDisabled");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/series/enumerate", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task FeatureOffAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/series/enumerate", member.AccessToken, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/lessons/les_anything", member.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/me/library/continue/enumerate", member.AccessToken, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/series", admin.AccessToken, new { title = "Nope" }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/public/lessons/enumerate", null, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
        }

        #endregion
    }
}
