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
    /// Forums under every access model (docs/ACCESS_MODELS_CONTRACT.md and docs/PHASE3_CONTRACT.md section 0.4): Open
    /// (every signed-in account reads and writes; Members content is SignedIn content), PartiallyGated with forums not
    /// gated (forum and thread audiences per caller, anonymous browsing of Public forums and threads, new forums take
    /// the property default audience) and with forums gated (reads and writes need a membership on every path, new
    /// forums default to Members), and FullyGated (a membership for everything, public sample threads for visitors).
    /// </summary>
    public static class ForumAccessSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "ForumAccess";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Forum access models (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Open.EveryAccount", "Open: a signed-in account without a membership reads, creates threads, comments, and votes; a Members forum is SignedIn content; signed-out callers without anonymous browsing get 401", OpenAsync),
                Case("PartiallyGated.Audiences", "PartiallyGated, forums not gated: free accounts read SignedIn forums and threads and take part, Members forums are hidden, visitors browse only Public forums and threads and cannot take part, new forums take the default audience", PartiallyGatedAsync),
                Case("PartiallyGated.ForumsGated", "PartiallyGated, forums gated: free accounts get 402 naming forums on reads and writes, thread comments are not reachable through the comment routes, visitors get 401, members are unaffected, new forums default to Members", GatedAsync),
                Case("FullyGated.MembersAndSamples", "FullyGated: free accounts get 402 on forum routes, visitors get 401 on member routes and see only public sample threads", FullyGatedAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                ForumTestKit.Relax(f);
                return body(f, ct);
            });
        }

        private static async Task OpenAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            string membersForum = Api.Str(await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Members Lounge", ct, new { audience = "Members" }).ConfigureAwait(false), "id");
            await SetAsync(f, new { model = "Open", anonymousBrowsing = false }, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            JsonElement forums = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/forums", free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(forums).Contains(membersForum), "A Members forum is listed to every account in Open");
            JsonElement forum = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/forums/" + membersForum, free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(forum.GetProperty("viewer").GetProperty("canCreateThread").GetBoolean(), "Free accounts may create threads in Open");
            JsonElement poll = await ForumTestKit.PollThreadAsync(f, free.AccessToken, membersForum, ct).ConfigureAwait(false);
            string pollId = Api.Str(poll, "id");
            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + pollId, free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(read.GetProperty("viewer").GetProperty("canComment").GetBoolean() && read.GetProperty("viewer").GetProperty("canVote").GetBoolean(), "Free accounts take part in Open");
            await ForumTestKit.CommentAsync(f, free.AccessToken, pollId, "free comment", null, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/polls/" + Api.Str(poll.GetProperty("poll"), "id") + "/votes", free.AccessToken,
                new { optionIds = new[] { Api.Str(poll.GetProperty("poll").GetProperty("options")[0], "id") } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/forums", null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
            JsonElement created = await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Open Default", ct).ConfigureAwait(false);
            Check.Equal("SignedIn", Api.Str(created, "audience"), "New forums take the Open default audience");
        }

        private static async Task PartiallyGatedAsync(HttpFixture f, CancellationToken ct)
        {
            await SetAsync(f, new { model = "PartiallyGated", anonymousBrowsing = true, gatedFeatures = Array.Empty<string>(), defaultAudience = "SignedIn" }, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement signedInForum = await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Community Talk", ct).ConfigureAwait(false);
            Check.Equal("SignedIn", Api.Str(signedInForum, "audience"), "New forums take access.defaultAudience");
            string signedInId = Api.Str(signedInForum, "id");
            string publicForum = Api.Str(await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Visitors Welcome", ct, new { audience = "Public" }).ConfigureAwait(false), "id");
            string membersForum = Api.Str(await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Members Room", ct, new { audience = "Members" }).ConfigureAwait(false), "id");
            string publicThread = Api.Str(await ForumTestKit.ThreadAsync(f, admin.AccessToken, publicForum, new { type = "Discussion", title = "Hello visitors", body = "Public thread", audience = "Public" }, ct).ConfigureAwait(false), "id");
            string membersThread = await ForumTestKit.ThreadIdAsync(f, member.AccessToken, membersForum, "Members talk", ct).ConfigureAwait(false);

            JsonElement freeForums = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/forums", free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> freeIds = ForumTestKit.Ids(freeForums);
            Check.True(freeIds.Contains(signedInId) && freeIds.Contains(publicForum) && !freeIds.Contains(membersForum), "Free accounts see SignedIn and Public forums, not Members forums");
            string freeThread = await ForumTestKit.ThreadIdAsync(f, free.AccessToken, signedInId, "Free account thread", ct).ConfigureAwait(false);
            await ForumTestKit.CommentAsync(f, free.AccessToken, freeThread, "free comment", null, ct).ConfigureAwait(false);
            ApiResult hidden = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/threads/" + membersThread, free.AccessToken, null, ct).ConfigureAwait(false);
            Check.True(hidden.Status != HttpStatusCode.OK, "Free accounts cannot open a thread in a Members forum (" + hidden.Status + ")");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + membersThread, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);

            JsonElement visitorForums = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/forums", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(visitorForums).SequenceEqual(new[] { publicForum }), "Visitors list only Public forums");
            JsonElement visitorThread = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + publicThread, null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement viewer = visitorThread.GetProperty("viewer");
            Check.True(!viewer.GetProperty("canComment").GetBoolean() && !viewer.GetProperty("canReact").GetBoolean() && !viewer.GetProperty("canFollow").GetBoolean(), "Visitors cannot take part");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + publicThread + "/comments", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult visitorPrivate = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/threads/" + freeThread, null, null, ct).ConfigureAwait(false);
            Check.True(visitorPrivate.Status != HttpStatusCode.OK, "Visitors cannot open a SignedIn thread (" + visitorPrivate.Status + ")");
            ApiResult visitorWrite = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/forums/" + publicForum + "/threads", null, new { type = "Discussion", title = "Visitor", body = "x" }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Unauthorized, visitorWrite.Status, "Visitors cannot create threads");
            Check.Equal("SignIn", Api.Str(visitorWrite.Body, "required"), "required SignIn");
        }

        private static async Task GatedAsync(HttpFixture f, CancellationToken ct)
        {
            await SetAsync(f, new { model = "PartiallyGated", anonymousBrowsing = true, gatedFeatures = new[] { "forums" }, defaultAudience = "SignedIn" }, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement gatedForum = await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Gated Talk", ct).ConfigureAwait(false);
            Check.Equal("Members", Api.Str(gatedForum, "audience"), "A gated feature defaults new forums to Members");
            string publicForum = Api.Str(await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Gated Public", ct, new { audience = "Public" }).ConfigureAwait(false), "id");
            string publicThread = Api.Str(await ForumTestKit.ThreadAsync(f, admin.AccessToken, publicForum, new { type = "Discussion", title = "Public but gated", body = "Public thread", audience = "Public" }, ct).ConfigureAwait(false), "id");
            string comment = await ForumTestKit.CommentIdAsync(f, member.AccessToken, publicThread, "member comment", ct).ConfigureAwait(false);

            ApiResult list = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/forums", free.AccessToken, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.PaymentRequired, list.Status, "Free accounts: forum list (" + list.Text + ")");
            Check.Equal("Membership", Api.Str(list.Body, "required"), "required Membership");
            Check.Equal("forums", Api.Str(list.Body, "feature"), "feature forums");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/threads/" + publicThread, free.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/forums/" + publicForum + "/threads", free.AccessToken, new { type = "Discussion", title = "Free", body = "x" }, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
            // Search is its own feature: gating forums leaves search open, but its thread and comment results follow the forum gate.
            JsonElement freeSearch = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", free.AccessToken, new { search = "gated" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!freeSearch.GetProperty("objects").EnumerateArray().Any(o => Api.Str(o, "type") == "Thread" || Api.Str(o, "type") == "Comment"), "Gated forums keep their threads out of a free account's search");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/comments/" + comment, free.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/comments/" + comment + "/reactions/like", free.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);

            ApiResult visitor = await Api.SendAsync(f, HttpMethod.Get, "/api/v1/forums", null, null, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Unauthorized, visitor.Status, "Visitors cannot browse a gated feature (" + visitor.Text + ")");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/threads/" + publicThread, null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);

            JsonElement memberList = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/forums", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ForumTestKit.Ids(memberList).Contains(publicForum) && ForumTestKit.Ids(memberList).Contains(Api.Str(gatedForum, "id")), "Members see the gated forums");
            JsonElement memberThread = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + publicThread, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(memberThread.GetProperty("viewer").GetProperty("canComment").GetBoolean(), "Members take part");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/comments/" + comment, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);

            // A promoted card of a gated forum's thread is skipped in a free account's feed list (no locked stub that names the thread).
            string promoted = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/threads/" + publicThread + "/promote", admin.AccessToken, new { notifyMembers = false }, HttpStatusCode.Created, ct).ConfigureAwait(false), "id");
            JsonElement freeFeed = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed?maxResults=50", free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!freeFeed.GetProperty("objects").EnumerateArray().Any(o => Api.Str(o, "id") == promoted), "Free accounts do not see promoted cards of gated forums");
            JsonElement memberFeed = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed?maxResults=50", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(memberFeed.GetProperty("objects").EnumerateArray().Any(o => Api.Str(o, "id") == promoted), "Members see the promoted card");
        }

        private static async Task FullyGatedAsync(HttpFixture f, CancellationToken ct)
        {
            await SetAsync(f, new { model = "FullyGated" }, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement created = await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Fully Gated Default", ct).ConfigureAwait(false);
            Check.Equal("Members", Api.Str(created, "audience"), "New forums are Members in FullyGated");
            string publicForum = Api.Str(await ForumTestKit.CreateForumAsync(f, admin.AccessToken, "Sample Corner", ct, new { audience = "Public" }).ConfigureAwait(false), "id");
            string sample = Api.Str(await ForumTestKit.ThreadAsync(f, admin.AccessToken, publicForum, new { type = "Discussion", title = "Sample thread", body = "A teaser", audience = "Public" }, ct).ConfigureAwait(false), "id");
            string membersThread = Api.Str(await ForumTestKit.ThreadAsync(f, member.AccessToken, publicForum, new { type = "Discussion", title = "Member thread in the sample forum", body = "Members only", audience = "Members" }, ct).ConfigureAwait(false), "id");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/forums", free.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/threads/" + sample, free.AccessToken, null, HttpStatusCode.PaymentRequired, "MembershipRequired", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/threads/" + sample, null, null, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/threads/" + sample, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult samples = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/public/threads/enumerate", null, new { }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, samples.Status, "Public samples (" + samples.Text + ")");
            List<string> ids = ForumTestKit.Ids(samples.Body);
            Check.True(ids.Contains(sample) && !ids.Contains(membersThread), "Only Public threads in Public forums are samples");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/threads/" + sample, null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task<JsonElement> SetAsync(HttpFixture f, object body, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/property/access", HttpFixture.AdminToken, body, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        #endregion
    }
}
