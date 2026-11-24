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

    /// <summary>
    /// Phase 2 admin feed management (contract section 10.1): the admin enumeration with filters, orderings, and exact
    /// totals; create as an author; detail with bounded previews; publish now; engagement metrics; and audit records.
    /// </summary>
    public static class AdminFeedSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "AdminFeed";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Admin feed (" + TestEnvironment.Provider + ")", new[]
            {
                Case("AdminFeed.List", "The admin feed lists every state including Deleted, filters by state, author, pinned, and time, orders by counts, and reports exact totals", ListAsync),
                Case("AdminFeed.Windows", "The standard createdAfterUtc and createdBeforeUtc window applies to createdUtc (GET and POST); publishedAfterUtc and publishedBeforeUtc and their aliases fromUtc and toUtc filter the published window; a name and its alias with different values is 400; audience filters", WindowsAsync),
                Case("AdminFeed.Create", "Admin create as authorUserId needs canPostToFeed or SystemAdmin; the admin token cannot author; members are refused", CreateAsync),
                Case("AdminFeed.Detail", "The detail carries deletedUtc, bounded revision and moderation action previews with enumerate pointers, and reportSummary", DetailAsync),
                Case("AdminFeed.PublishNow", "publish-now publishes Scheduled posts and returns 409 for other states", PublishNowAsync),
                Case("AdminFeed.Engagement", "Engagement metrics return one entry per day and validate days", EngagementAsync),
                Case("AdminFeed.Audit", "feedPost.create, update, delete, pin, unpin, and publish are audited", AuditAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                ContentTestKit.Relax(f);
                return body(f, ct);
            });
        }

        private static async Task ListAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string published = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "published", ct).ConfigureAwait(false);
            string scheduled = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "scheduled", publishAtUtc = DateTime.UtcNow.AddDays(1) }, ct).ConfigureAwait(false), "id");
            string deleted = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "deleted", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/feed/" + deleted, poster.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            string pinned = Api.Str(await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "pinned", pin = true, labels = new[] { "staff:hot" } }, ct).ConfigureAwait(false), "id");
            await ContentTestKit.CommentIdAsync(f, admin.AccessToken, published, "c1", null, ct).ConfigureAwait(false);
            await ContentTestKit.CommentIdAsync(f, admin.AccessToken, published, "c2", null, ct).ConfigureAwait(false);
            JsonElement all = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/feed", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(4L, all.GetProperty("totalRecords").GetInt64(), "Every state counted");
            Check.Equal(0L, all.GetProperty("recordsRemaining").GetInt64(), "recordsRemaining");
            JsonElement item = all.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "id") == published);
            foreach (string field in new[] { "kind", "title", "excerpt", "author", "state", "visibility", "isPinned", "publishAtUtc", "publishedUtc", "createdUtc", "editedUtc", "attachmentCount", "hasVideo", "commentCount", "reactionTotal", "openReportCount", "labels", "tags" })
                Check.True(item.TryGetProperty(field, out JsonElement _), "Admin item field " + field);
            Check.True(Ids(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/feed?state=Deleted", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SequenceEqual(new[] { deleted }), "state=Deleted");
            Check.True(Ids(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/feed?state=Scheduled", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SequenceEqual(new[] { scheduled }), "state=Scheduled");
            Check.True(Ids(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/feed?pinned=true", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SequenceEqual(new[] { pinned }), "pinned=true");
            Check.Equal(3, Ids(await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/feed/enumerate", admin.AccessToken, new { filters = new Dictionary<string, string> { { "authorUserId", poster.Id } } }, HttpStatusCode.OK, ct).ConfigureAwait(false)).Count, "authorUserId");
            Check.Equal(published, Ids(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/feed?ordering=CommentCountDescending", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false))[0], "CommentCountDescending");
            Check.Equal(0, Ids(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/feed?fromUtc=" + Uri.EscapeDataString(DateTime.UtcNow.AddDays(2).ToString("o")), admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).Count, "fromUtc");
            Check.True(Ids(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/feed?labels=staff:hot", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SequenceEqual(new[] { pinned }), "Staff label filter");
            JsonElement skipped = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/feed?maxResults=1&skip=2&ordering=CreatedAscending", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(Ids(skipped).SequenceEqual(new[] { deleted }), "skip");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/feed?state=Lost", admin.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/feed", poster.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/feed", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task WindowsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string older = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "older", ct).ConfigureAwait(false);
            string newer = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "newer", audience = "SignedIn" }, ct).ConfigureAwait(false), "id");
            await f.Raw.ExecuteQueryAsync("UPDATE feedposts SET createdutc = '2026-01-01T00:00:00.000Z', publishedutc = '2026-06-01T00:00:00.000Z' WHERE id = '" + older + "';", false, ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE feedposts SET createdutc = '2026-03-01T00:00:00.000Z', publishedutc = '2026-03-02T00:00:00.000Z' WHERE id = '" + newer + "';", false, ct).ConfigureAwait(false);
            string q = "/api/v1/admin/feed?authorUserId=" + poster.Id;

            Check.True(Ids(await Api.ExpectAsync(f, HttpMethod.Get, q + "&createdAfterUtc=2026-02-01T00:00:00Z", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SequenceEqual(new[] { newer }), "GET createdAfterUtc on createdUtc");
            Check.True(Ids(await Api.ExpectAsync(f, HttpMethod.Get, q + "&createdBeforeUtc=2026-02-01T00:00:00Z", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SequenceEqual(new[] { older }), "GET createdBeforeUtc on createdUtc");
            JsonElement posted = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/feed/enumerate", admin.AccessToken,
                new { createdAfterUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), filters = new Dictionary<string, string> { { "authorUserId", poster.Id } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(Ids(posted).SequenceEqual(new[] { newer }), "POST createdAfterUtc on createdUtc");
            Check.True(Ids(await Api.ExpectAsync(f, HttpMethod.Get, q + "&publishedAfterUtc=2026-04-01T00:00:00Z", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SequenceEqual(new[] { older }), "publishedAfterUtc on publishedUtc");
            Check.True(Ids(await Api.ExpectAsync(f, HttpMethod.Get, q + "&fromUtc=2026-04-01T00:00:00Z", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SequenceEqual(new[] { older }), "fromUtc alias");
            Check.True(Ids(await Api.ExpectAsync(f, HttpMethod.Get, q + "&toUtc=2026-04-01T00:00:00Z", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SequenceEqual(new[] { newer }), "toUtc alias");
            Check.True(Ids(await Api.ExpectAsync(f, HttpMethod.Get, q + "&publishedBeforeUtc=2026-04-01T00:00:00Z&toUtc=2026-04-01T00:00:00Z", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SequenceEqual(new[] { newer }), "A name and its alias with the same value");
            Check.Equal(0, Ids(await Api.ExpectAsync(f, HttpMethod.Get, q + "&createdAfterUtc=2026-02-01T00:00:00Z&publishedAfterUtc=2026-04-01T00:00:00Z", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).Count, "Both windows apply together");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, q + "&publishedAfterUtc=2026-04-01T00:00:00Z&fromUtc=2026-05-01T00:00:00Z", admin.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, q + "&createdAfterUtc=2026-04-01T00:00:00Z&createdBeforeUtc=2026-03-01T00:00:00Z", admin.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            Check.True(Ids(await Api.ExpectAsync(f, HttpMethod.Get, q + "&audience=SignedIn", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).SequenceEqual(new[] { newer }), "audience filter");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, q + "&audience=Everyone", admin.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        private static async Task CreateAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember plain = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement mine = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/feed", admin.AccessToken, new { body = "as me" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.Equal(admin.Id, Api.Str(mine.GetProperty("author"), "id"), "Default author is the caller");
            JsonElement theirs = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/feed", admin.AccessToken, new { body = "for the poster", authorUserId = poster.Id, visibility = "Public" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.Equal(poster.Id, Api.Str(theirs.GetProperty("author"), "id"), "authorUserId");
            Check.Equal("Public", Api.Str(theirs, "visibility"), "Admin may set Public for another author");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/feed", admin.AccessToken, new { body = "x", authorUserId = plain.Id }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/feed", admin.AccessToken, new { body = "x", authorUserId = "usr_missing" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/feed", HttpFixture.AdminToken, new { body = "token" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/feed", poster.AccessToken, new { body = "member" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement edited = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/feed/" + Api.Str(theirs, "id"), HttpFixture.AdminToken, new { title = "Token edit" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Token edit", Api.Str(edited, "title"), "The admin token may edit");
            f.Settings.Feed.PostsPerHour = 1;
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/feed", admin.AccessToken, new { body = "admins are exempt" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
        }

        private static async Task DetailAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember reporter = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            f.Settings.Content.EditGraceSeconds = 0;
            string id = await ContentTestKit.PostIdAsync(f, admin.AccessToken, "v0", ct).ConfigureAwait(false);
            for (int i = 1; i <= 7; i++) await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/feed/" + id, admin.AccessToken, new { body = "v" + i }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string now = NotificationTestKit.Ts(DateTime.UtcNow);
            await f.Raw.ExecuteQueryAsync("INSERT INTO reports (propertyid, id, reporteruserid, targettype, targetid, targetauthoruserid, scopetype, scopeid, reason, status, dueutc, createdutc) VALUES ('" + TestProperty.SampleId + "', 'rpt_test1', '" + reporter.Id +
                "', 'FeedPost', '" + id + "', '" + admin.Id + "', 'Feed', 'feed', 'Spam', 'Open', " + now + ", " + now + ");", false, ct).ConfigureAwait(false);
            Check.Equal(1, await f.Services.Feed.RecomputeOpenReportCountAsync(id, ct).ConfigureAwait(false), "openreportcount recomputed from reports");
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/feed/" + id, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement revisions = detail.GetProperty("revisions");
            Check.Equal(5, revisions.GetProperty("objects").GetArrayLength(), "At most 5 revisions");
            Check.Equal(7L, revisions.GetProperty("totalRecords").GetInt64(), "Total revisions");
            Check.Equal("/api/v1/feed/" + id + "/revisions/enumerate", Api.Str(revisions, "enumerate"), "Revisions pointer");
            Check.True(revisions.GetProperty("objects")[0].TryGetProperty("editedByName", out JsonElement _) && revisions.GetProperty("objects")[0].TryGetProperty("editedByUserId", out JsonElement _), "editedByUserId and editedByName");
            Check.Equal(0, detail.GetProperty("moderationActions").GetProperty("objects").GetArrayLength(), "No moderation actions");
            Check.True(Api.Str(detail.GetProperty("moderationActions"), "enumerate").Length > 0, "Moderation actions pointer");
            Check.Equal(1L, detail.GetProperty("reportSummary").GetProperty("open").GetInt64(), "Open reports");
            Check.Equal(1L, detail.GetProperty("reportSummary").GetProperty("total").GetInt64(), "Total reports");
            Check.Equal(1, detail.GetProperty("openReportCount").GetInt32(), "openReportCount");
            Check.Equal("None", Api.Str(detail.GetProperty("moderation"), "state"), "Moderator view moderation object");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/feed/" + id, admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            JsonElement gone = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/feed/" + id, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Deleted", Api.Str(gone, "state"), "Deleted state");
            Check.True(gone.GetProperty("deletedUtc").ValueKind == JsonValueKind.String, "deletedUtc");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + id + "/revisions", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/feed/fpo_missing", admin.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task PublishNowAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            string scheduled = Api.Str(await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "soon", publishAtUtc = DateTime.UtcNow.AddDays(3) }, ct).ConfigureAwait(false), "id");
            JsonElement now = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/feed/" + scheduled + "/publish-now", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Published", Api.Str(now, "state"), "Published now");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/feed/" + scheduled + "/publish-now", admin.AccessToken, null, (HttpStatusCode)409, "NotScheduled", ct).ConfigureAwait(false);
            string media = await ContentTestKit.MediaAsync(f, admin.Id, MediaKindEnum.Image, ct, MediaStatusEnum.Processing).ConfigureAwait(false);
            string waiting = Api.Str(await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "with media", mediaIds = new[] { media }, publishAtUtc = DateTime.UtcNow.AddDays(3) }, ct).ConfigureAwait(false), "id");
            JsonElement held = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/feed/" + waiting + "/publish-now", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Processing", Api.Str(held, "state"), "Still waiting for media");
            await f.Raw.ExecuteQueryAsync("UPDATE mediaattachments SET status = 'Ready', readyutc = " + NotificationTestKit.Ts(DateTime.UtcNow) + " WHERE id = '" + media + "';", false, ct).ConfigureAwait(false);
            Check.Equal(1, await ContentTestKit.PublishAsync(f, ct).ConfigureAwait(false), "The worker publishes once media is Ready");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/feed/fpo_missing/publish-now", admin.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task EngagementAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember m = await ContentTestKit.MemberAsync(f, ct).ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, admin.AccessToken, "today", ct).ConfigureAwait(false);
            await ContentTestKit.CommentIdAsync(f, m.AccessToken, post, "comment", null, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + post + "/reactions/like", m.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ContentTestKit.PostAsync(f, admin.AccessToken, new { body = "later", publishAtUtc = DateTime.UtcNow.AddDays(1) }, ct).ConfigureAwait(false);
            JsonElement metrics = await MetricEnumerationTestKit.DailyAsync(f, "/api/v1/admin/metrics/engagement?days=7", admin.AccessToken, ct).ConfigureAwait(false);
            Check.Equal(7, metrics.GetProperty("postsByDay").GetArrayLength(), "One entry per day");
            Check.Equal(7, metrics.GetProperty("commentsByDay").GetArrayLength(), "Comments per day");
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            JsonElement last = metrics.GetProperty("postsByDay")[6];
            Check.Equal(today, Api.Str(last, "date"), "Last day is today");
            Check.Equal(1L, last.GetProperty("count").GetInt64(), "Published today");
            Check.Equal(1L, metrics.GetProperty("commentsByDay")[6].GetProperty("count").GetInt64(), "Comment today");
            Check.Equal(1L, metrics.GetProperty("reactionsByDay")[6].GetProperty("count").GetInt64(), "Reaction today");
            Check.Equal(1L, metrics.GetProperty("activeMembers").GetInt64(), "Active members");
            Check.Equal(1L, metrics.GetProperty("scheduledPosts").GetInt64(), "Scheduled posts");
            Check.Equal(7, metrics.GetProperty("threadsByDay").GetArrayLength(), "Forums series: threads per day");
            Check.Equal(0L, metrics.GetProperty("threadsByDay")[6].GetProperty("count").GetInt64(), "No threads yet");
            Check.Equal(7, metrics.GetProperty("threadCommentsByDay").GetArrayLength(), "Forums series: thread comments per day");
            ForumTestKit.Relax(f);
            string forum = await ForumTestKit.DefaultForumIdAsync(f, m.AccessToken, ct).ConfigureAwait(false);
            string thread = await ForumTestKit.ThreadIdAsync(f, m.AccessToken, forum, "Engagement thread", ct).ConfigureAwait(false);
            await ForumTestKit.CommentIdAsync(f, admin.AccessToken, thread, "Thread reply", ct).ConfigureAwait(false);
            await ForumTestKit.CommentIdAsync(f, m.AccessToken, thread, "Second reply", ct).ConfigureAwait(false);
            JsonElement forums = await MetricEnumerationTestKit.DailyAsync(f, "/api/v1/admin/metrics/engagement?days=7", admin.AccessToken, ct).ConfigureAwait(false);
            Check.Equal(today, Api.Str(forums.GetProperty("threadsByDay")[6], "date"), "Forums series ends today");
            Check.Equal(1L, forums.GetProperty("threadsByDay")[6].GetProperty("count").GetInt64(), "Thread today");
            Check.Equal(2L, forums.GetProperty("threadCommentsByDay")[6].GetProperty("count").GetInt64(), "Thread comments today");
            Check.Equal(30, (await MetricEnumerationTestKit.DailyAsync(f, "/api/v1/admin/metrics/engagement", admin.AccessToken, ct).ConfigureAwait(false)).GetProperty("postsByDay").GetArrayLength(), "Default 30 days");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/metrics/engagement?days=3", admin.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/metrics/engagement?days=400", admin.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        private static async Task AuditAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await ContentTestKit.AdminAsync(f, ct).ConfigureAwait(false);
            string id = Api.Str(await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/feed", admin.AccessToken, new { body = "audit me" }, HttpStatusCode.Created, ct).ConfigureAwait(false), "id");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/feed/" + id, admin.AccessToken, new { body = "audited" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/feed/" + id + "/pin", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/feed/" + id + "/unpin", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/feed/" + id, admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            foreach (string action in new[] { "feedPost.create", "feedPost.update", "feedPost.pin", "feedPost.unpin", "feedPost.delete", "feedPost.publish" })
            {
                long count = await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action = '" + action + "' AND targetid = '" + id + "';", ct).ConfigureAwait(false);
                Check.Equal(1L, count, "Audit " + action);
            }
        }

        private static List<string> Ids(JsonElement page)
        {
            return ContentTestKit.Ids(page);
        }

        #endregion
    }
}
