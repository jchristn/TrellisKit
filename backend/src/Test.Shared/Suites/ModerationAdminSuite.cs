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
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Phase 2 moderation console (contract sections 9.1, 9.3, 10.2, 10.4, and 11): queue orderings, filters, scope,
    /// paging, and labels; queue detail previews; dismiss; the actions log; content history; overdue notices once with a
    /// fixed clock; the open and overdue summary; moderators grant and revoke with immediate effect; the removed-content
    /// purge; account deletion effects; the admin feed actions preview; and the member search limit alias.
    /// </summary>
    public static class ModerationAdminSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "ModerationAdmin";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Moderation console (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Queue.OrderingsAndFilters", "One row per reported target with counts, reasons, and due times; DueAscending puts overdue first; filters status, targetType, reason, overdue; members get 403", QueueAsync),
                Case("Queue.PagingAndMetadata", "The queue pages with continuation tokens without gaps or duplicates, clamps maxResults, rejects unknown orderings and filters, and filters on the target's labels and tags", QueuePagingAsync),
                Case("Queue.DetailAndDismiss", "The detail carries the target in any state, context, bounded previews with enumerate paths, and author history; dismiss resolves Open reports once", DetailAsync),
                Case("Actions.LogAndContent", "The actions log filters by moderator, action, target type, and system; content history lists a member's posts and comments in any state", LogAndContentAsync),
                Case("Overdue.Notices", "Reports past the review target notify every moderator in scope once (fixed clock); the summary shows open and overdue counts", OverdueAsync),
                Case("Moderators.GrantRevoke", "Grant is idempotent, audited, and effective immediately; the list shows roles and statistics; revoke removes access; only admins manage moderators", ModeratorsAsync),
                Case("Purge.Worker", "Removed and deleted content older than retainRemovedDays without Open reports is purged with its media; purged content cannot be restored", PurgeAsync),
                Case("Account.Deletion", "Deleting an account erases comments, deletes posts and unattached media, and keeps filed reports with the reporter shown as Deleted member", AccountDeletionAsync),
                Case("Integration.PreviewAndSearch", "The admin feed detail shows the moderation actions preview with its enumerate path; member search accepts limit as an alias of maxResults", PreviewAndSearchAsync),
                Case("Regression.API04.FeedModeratorScope", "FeedModerators see and dismiss only member content in the Feed: User targets and SystemAdmin content are absent from their queue and 403 on detail, reports, and dismiss", async (f, ct) =>
                {
                    f.Settings.Moderation.AutoHideReportThreshold = 0;
                    TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
                    TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
                    TestMember moderator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
                    TestMember reporter = await ContentTestKit.MemberAsync(f, ct, "Reporter").ConfigureAwait(false);
                    string memberPost = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Member post", ct).ConfigureAwait(false);
                    string adminPost = await ContentTestKit.PostIdAsync(f, admin.AccessToken, "Admin post", ct).ConfigureAwait(false);
                    await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "FeedPost", memberPost, ct).ConfigureAwait(false);
                    await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "FeedPost", adminPost, ct).ConfigureAwait(false);
                    await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "User", poster.Id, ct, "Impersonation").ConfigureAwait(false);

                    JsonElement scoped = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue, moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(ModerationTestKit.TargetIds(scoped).SequenceEqual(new[] { memberPost }), "Only the member post is in the moderator's queue");
                    Check.Equal(1L, scoped.GetProperty("totalRecords").GetInt64(), "Totals match the scope");
                    JsonElement all = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(3L, all.GetProperty("totalRecords").GetInt64(), "SystemAdmin sees every target");

                    foreach (string path in new[] { ModerationTestKit.Queue + "/User/" + poster.Id, ModerationTestKit.Queue + "/FeedPost/" + adminPost })
                    {
                        await Api.ExpectErrorAsync(f, HttpMethod.Post, path + "/dismiss", moderator.AccessToken, new { note = "not mine" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                        await Api.ExpectErrorAsync(f, HttpMethod.Get, path, moderator.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                        await Api.ExpectErrorAsync(f, HttpMethod.Get, path + "/reports", moderator.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                    }
                    Check.Equal(2L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM reports WHERE status = 'Open' AND targetid IN ('" + poster.Id + "', '" + adminPost + "');", ct).ConfigureAwait(false), "Out-of-scope reports stay Open");
                    await Api.ExpectAsync(f, HttpMethod.Post, ModerationTestKit.Queue + "/FeedPost/" + memberPost + "/dismiss", moderator.AccessToken, new { note = "fine" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    await Api.ExpectAsync(f, HttpMethod.Post, ModerationTestKit.Queue + "/User/" + poster.Id + "/dismiss", admin.AccessToken, new { note = "fine" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),
                Case("Regression.API05.NoteLength", "A moderation note longer than 500 characters is 400 ValidationFailed (fieldErrors.note) on dismiss and on actions", async (f, ct) =>
                {
                    TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
                    TestMember moderator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
                    TestMember reporter = await ContentTestKit.MemberAsync(f, ct, "Reporter").ConfigureAwait(false);
                    string post = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Noted post", ct).ConfigureAwait(false);
                    await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "FeedPost", post, ct).ConfigureAwait(false);
                    string tooLong = new string('n', 501);
                    ApiResult dismiss = await Api.SendAsync(f, HttpMethod.Post, ModerationTestKit.Queue + "/FeedPost/" + post + "/dismiss", moderator.AccessToken, new { note = tooLong }, ct).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, dismiss.Status, "Dismiss status");
                    Check.Equal("ValidationFailed", dismiss.Error, "Dismiss error");
                    Check.True(dismiss.Body.GetProperty("fieldErrors").TryGetProperty("note", out JsonElement _), "fieldErrors.note on dismiss");
                    ApiResult act = await ModerationTestKit.ActAsync(f, moderator.AccessToken, "FeedPost", post, "Hide", ct, "Spam", null, null, tooLong).ConfigureAwait(false);
                    Check.Equal(HttpStatusCode.BadRequest, act.Status, "Action status");
                    Check.True(act.Body.GetProperty("fieldErrors").TryGetProperty("note", out JsonElement _), "fieldErrors.note on actions");
                    Check.Equal("Open", Api.Str((await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "/FeedPost/" + post + "/reports", moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("objects")[0], "status"), "Nothing dismissed");
                    await Api.ExpectAsync(f, HttpMethod.Post, ModerationTestKit.Queue + "/FeedPost/" + post + "/dismiss", moderator.AccessToken, new { note = new string('n', 500) }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }),
                Case("Export.Sections", "The account export carries feedPosts, comments, reactions, notificationPreferences, pushTokens (no token values), blocks, reports, and media", ExportAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, (f, ct) =>
            {
                ContentTestKit.Relax(f);
                f.Settings.Moderation.ReportsPerHour = 10000;
                return body(f, ct);
            });
        }

        private static async Task QueueAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Moderation.AutoHideReportThreshold = 0;
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember moderator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            TestMember member = await ContentTestKit.MemberAsync(f, ct, "Plain").ConfigureAwait(false);
            List<TestMember> reporters = await ModerationTestKit.MembersAsync(f, 3, ct).ConfigureAwait(false);
            string a = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Post A", ct).ConfigureAwait(false);
            string b = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Post B", ct).ConfigureAwait(false);
            string c = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, a, "Comment C", null, ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporters[0].AccessToken, "FeedPost", a, ct, "Spam").ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporters[1].AccessToken, "FeedPost", a, ct, "Spam").ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporters[2].AccessToken, "FeedPost", a, ct, "Harassment").ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporters[0].AccessToken, "FeedPost", b, ct, "Violence").ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporters[1].AccessToken, "Comment", c, ct, "HateSpeech").ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporters[2].AccessToken, "User", poster.Id, ct, "Impersonation").ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE reports SET dueutc = '2000-01-01T00:00:00.000Z' WHERE targetid = '" + c + "';", false, ct).ConfigureAwait(false);
            TestMember staff = await Api.AdminAsync(f, ct).ConfigureAwait(false);

            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue, staff.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> ids = ModerationTestKit.TargetIds(page);
            Check.Equal(4, ids.Count, "One row per reported target");
            Check.Equal(4L, page.GetProperty("totalRecords").GetInt64(), "Exact totals");
            Check.Equal(c, ids[0], "Overdue row first (DueAscending)");
            JsonElement rowA = page.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "targetId") == a);
            Check.Equal(3L, rowA.GetProperty("reportCount").GetInt64(), "Report count");
            Check.Equal(3L, rowA.GetProperty("reporterCount").GetInt64(), "Reporter count");
            Check.Equal(2L, rowA.GetProperty("reasons").GetProperty("Spam").GetInt64(), "Reasons Spam");
            Check.Equal(1L, rowA.GetProperty("reasons").GetProperty("Harassment").GetInt64(), "Reasons Harassment");
            Check.Equal("Published", Api.Str(rowA, "targetState"), "Target state");
            Check.Equal("Post A", Api.Str(rowA.GetProperty("preview"), "excerpt"), "Preview excerpt");
            Check.Equal(poster.Id, Api.Str(rowA.GetProperty("author"), "id"), "Author");
            Check.Equal("Open", Api.Str(rowA, "status"), "Status");
            ContentTestKit.CheckMetadata(rowA, "Queue row");
            JsonElement rowC = page.GetProperty("objects")[0];
            Check.True(rowC.GetProperty("isOverdue").GetBoolean(), "isOverdue");
            JsonElement rowUser = page.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "targetType") == "User");
            Check.Equal("Active", Api.Str(rowUser, "targetState"), "Member target state");

            JsonElement byCount = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "?ordering=ReportCountDescending", staff.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(a, ModerationTestKit.TargetIds(byCount)[0], "ReportCountDescending");
            JsonElement byLast = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "?ordering=LastReportedDescending", staff.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(poster.Id, ModerationTestKit.TargetIds(byLast)[0], "LastReportedDescending");
            JsonElement byFirst = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "?ordering=FirstReportedAscending", staff.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(a, ModerationTestKit.TargetIds(byFirst)[0], "FirstReportedAscending");
            JsonElement comments = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "?targetType=Comment", staff.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ModerationTestKit.TargetIds(comments).SequenceEqual(new[] { c }), "targetType filter");
            JsonElement violence = await Api.ExpectAsync(f, HttpMethod.Post, ModerationTestKit.Queue + "/enumerate", staff.AccessToken, new { filters = new { reason = "Violence" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ModerationTestKit.TargetIds(violence).SequenceEqual(new[] { b }), "reason filter");
            JsonElement overdue = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "?overdue=true", staff.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ModerationTestKit.TargetIds(overdue).SequenceEqual(new[] { c }), "overdue filter");
            JsonElement notOverdue = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "?overdue=false", staff.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(3, ModerationTestKit.TargetIds(notOverdue).Count, "overdue false");

            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "FeedPost", b, "Hide", ct).ConfigureAwait(false);
            JsonElement open = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue, moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!ModerationTestKit.TargetIds(open).Contains(b), "Resolved targets leave the Open queue");
            JsonElement resolved = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "?status=Resolved", moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ModerationTestKit.TargetIds(resolved).SequenceEqual(new[] { b }), "status Resolved");
            Check.Equal("Resolved", Api.Str(resolved.GetProperty("objects")[0], "status"), "Resolved row status");
            Check.Equal("Hidden", Api.Str(resolved.GetProperty("objects")[0], "targetState"), "Hidden state");

            await Api.ExpectErrorAsync(f, HttpMethod.Get, ModerationTestKit.Queue, member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "?status=Closed", moderator.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task QueuePagingAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Moderation.AutoHideReportThreshold = 0;
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember reporter = await ContentTestKit.MemberAsync(f, ct, "Reporter").ConfigureAwait(false);
            List<string> posts = new List<string>();
            for (int i = 0; i < 7; i++)
            {
                string id = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "post " + i, ct).ConfigureAwait(false);
                posts.Add(id);
                await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "FeedPost", id, ct).ConfigureAwait(false);
            }
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/FeedPost/" + posts[3], admin.AccessToken, new { labels = new[] { "flagged" }, tags = new Dictionary<string, string> { { "staff:priority", "high" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);

            List<string> seen = new List<string>();
            string? next = null;
            int pages = 0;
            do
            {
                JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, ModerationTestKit.Queue + "/enumerate", admin.AccessToken, new { maxResults = 3, continuationToken = next, ordering = "FirstReportedAscending" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                seen.AddRange(ModerationTestKit.TargetIds(page));
                next = page.GetProperty("continuationToken").ValueKind == JsonValueKind.String ? page.GetProperty("continuationToken").GetString() : null;
                if (pages == 0)
                {
                    string extra = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "late", ct).ConfigureAwait(false);
                    await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "FeedPost", extra, ct).ConfigureAwait(false);
                    posts.Add(extra);
                }
                pages++;
            }
            while (next != null && pages < 10);
            Check.True(seen.SequenceEqual(posts), "Every row once in order across pages, including one reported mid-paging");

            JsonElement clamped = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "?maxResults=5000", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(f.Settings.Enumeration.MaxResultsAdmin, clamped.GetProperty("maxResults").GetInt32(), "maxResults clamped to the admin maximum");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "?ordering=Newest", admin.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "?color=red", admin.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement labeled = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "?labels=flagged", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ModerationTestKit.TargetIds(labeled).SequenceEqual(new[] { posts[3] }), "Label filter on the target");
            JsonElement tagged = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "?tag.staff:priority=*", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ModerationTestKit.TargetIds(tagged).SequenceEqual(new[] { posts[3] }), "Staff tag presence filter on the target");
            JsonElement any = await Api.ExpectAsync(f, HttpMethod.Post, ModerationTestKit.Queue + "/enumerate", admin.AccessToken, new { labels = new[] { "flagged", "nothing" }, labelMatch = "Any" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, ModerationTestKit.TargetIds(any).Count, "labelMatch Any");
            JsonElement row = labeled.GetProperty("objects")[0];
            Check.True(row.GetProperty("labels").EnumerateArray().Any(l => l.GetString() == "flagged"), "Row carries the target's labels");
            Check.Equal("high", Api.Str(row.GetProperty("tags"), "staff:priority"), "Staff tags shown to moderators");
        }

        private static async Task DetailAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Moderation.AutoHideReportThreshold = 0;
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember moderator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Original body", ct).ConfigureAwait(false);
            string parent = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, postId, "parent", null, ct).ConfigureAwait(false);
            string commentId = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, postId, "reported reply", parent, ct).ConfigureAwait(false);
            List<TestMember> reporters = await ModerationTestKit.MembersAsync(f, 6, ct).ConfigureAwait(false);
            foreach (TestMember r in reporters) await ModerationTestKit.ReportOkAsync(f, r.AccessToken, "Comment", commentId, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", reporters[0].AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);

            string path = ModerationTestKit.Queue + "/Comment/" + commentId;
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, path, moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(commentId, Api.Str(detail.GetProperty("target"), "id"), "Target comment");
            Check.Equal(postId, Api.Str(detail.GetProperty("context"), "feedPostId"), "Context post");
            Check.Equal(parent, Api.Str(detail.GetProperty("context"), "parentCommentId"), "Context parent");
            JsonElement reports = detail.GetProperty("reports");
            Check.Equal(5, reports.GetProperty("objects").GetArrayLength(), "At most 5 reports in the preview");
            Check.Equal(6L, reports.GetProperty("totalRecords").GetInt64(), "Total reports");
            Check.Equal(path + "/reports/enumerate", Api.Str(reports, "enumerate"), "Reports enumerate path");
            Check.Equal(path + "/actions/enumerate", Api.Str(detail.GetProperty("actions"), "enumerate"), "Actions enumerate path");
            Check.Equal("/api/v1/comments/" + commentId + "/revisions/enumerate", Api.Str(detail.GetProperty("revisions"), "enumerate"), "Revisions enumerate path");
            Check.Equal(6L, detail.GetProperty("authorHistory").GetProperty("reportsAgainst90d").GetInt64(), "Reports against the author");
            Check.Equal(6L, detail.GetProperty("openReports").GetInt64(), "Open reports");
            ContentTestKit.CheckMetadata(detail, "Detail");

            JsonElement all = await Api.ExpectAsync(f, HttpMethod.Post, path + "/reports/enumerate", moderator.AccessToken, new { maxResults = 10, ordering = "CreatedAscending" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(6, all.GetProperty("objects").GetArrayLength(), "Every report");
            JsonElement first = all.GetProperty("objects")[0];
            Check.True(first.GetProperty("reporter").GetProperty("isDeleted").GetBoolean(), "A deleted reporter shows as Deleted member");
            ContentTestKit.CheckMetadata(first, "Report view");

            JsonElement dismissed = await Api.ExpectAsync(f, HttpMethod.Post, path + "/dismiss", moderator.AccessToken, new { note = "Not a problem" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0L, dismissed.GetProperty("openReports").GetInt64(), "No Open reports after dismiss");
            Check.Equal(1L, dismissed.GetProperty("actions").GetProperty("totalRecords").GetInt64(), "A Dismiss action");
            Check.Equal("Dismiss", Api.Str(dismissed.GetProperty("actions").GetProperty("objects")[0], "action"), "Dismiss action row");
            Check.Equal(6L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM reports WHERE targetid = '" + commentId + "' AND status = 'Dismissed' AND resolutionnote = 'Not a problem';", ct).ConfigureAwait(false), "Reports Dismissed with the note");
            Check.Equal("None", await ModerationTestKit.StateAsync(f, "comments", commentId, ct).ConfigureAwait(false), "Content unchanged");
            Check.Equal(1L, await ModerationTestKit.AuditCountAsync(f, "moderation.dismiss", commentId, ct).ConfigureAwait(false), "moderation.dismiss audited");
            JsonElement again = await Api.ExpectAsync(f, HttpMethod.Post, path + "/dismiss", moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, again.GetProperty("actions").GetProperty("totalRecords").GetInt64(), "A second dismiss records nothing");
            JsonElement resolvedReport = (await Api.ExpectAsync(f, HttpMethod.Get, path + "/reports?status=Dismissed&maxResults=1", moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false)).GetProperty("objects")[0];
            Check.Equal("Feed Moderator", Api.Str(resolvedReport, "resolvedByName"), "Resolver name");

            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + postId, poster.AccessToken, new { body = "Edited body" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE feedposts SET publishedutc = '2000-01-01T00:00:00.000Z' WHERE id = '" + postId + "';", false, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + postId, poster.AccessToken, new { body = "Edited again" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "FeedPost", postId, "Remove", ct).ConfigureAwait(false);
            JsonElement postDetail = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "/FeedPost/" + postId, moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Removed", Api.Str(postDetail.GetProperty("target").GetProperty("moderation"), "state"), "Removed post visible to moderators");
            Check.True(postDetail.GetProperty("revisions").GetProperty("totalRecords").GetInt64() >= 1, "Revision preview");
            Check.Equal(1L, postDetail.GetProperty("authorHistory").GetProperty("removedCount90d").GetInt64(), "Removed count");
            JsonElement userDetail = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "/User/" + poster.Id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(poster.Id, Api.Str(userDetail.GetProperty("target"), "id"), "Member target is a UserSummary");
            Check.Equal(0, userDetail.GetProperty("revisions").GetProperty("objects").GetArrayLength(), "Members have no revisions");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "/Media/med_x", moderator.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "/FeedPost/fpo_missing", moderator.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task LogAndContentAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Moderation.AutoHideReportThreshold = 1;
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember moderator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember reporter = await ContentTestKit.MemberAsync(f, ct, "Reporter").ConfigureAwait(false);
            string p1 = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "first", ct).ConfigureAwait(false);
            string p2 = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "second", ct).ConfigureAwait(false);
            string p3 = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "third", ct).ConfigureAwait(false);
            string c1 = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, p1, "comment one", null, ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "FeedPost", p1, ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "FeedPost", p2, "Remove", ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, admin.AccessToken, "Comment", c1, "Hide", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/feed/" + p3, poster.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);

            JsonElement log = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Actions, moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(3L, log.GetProperty("totalRecords").GetInt64(), "Every action");
            Check.Equal("Hide", Api.Str(log.GetProperty("objects")[0], "action"), "Newest first");
            JsonElement byModerator = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Actions + "?moderatorUserId=" + moderator.Id, moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, byModerator.GetProperty("totalRecords").GetInt64(), "moderatorUserId filter");
            JsonElement system = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Actions + "?moderatorUserId=system", moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("AutoHide", Api.Str(system.GetProperty("objects")[0], "action"), "System actions");
            Check.True(system.GetProperty("objects")[0].GetProperty("moderator").ValueKind == JsonValueKind.Null, "No moderator on system actions");
            JsonElement comments = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Actions + "?targetType=Comment&action=Hide", moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, comments.GetProperty("totalRecords").GetInt64(), "targetType and action filters");
            JsonElement window = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Actions + "?createdAfterUtc=" + Uri.EscapeDataString(DateTime.UtcNow.AddDays(1).ToString("O")), moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0L, window.GetProperty("totalRecords").GetInt64(), "Created window");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, ModerationTestKit.Actions + "?action=Delete", moderator.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);

            string content = "/api/v1/admin/moderation/content";
            await Api.ExpectErrorAsync(f, HttpMethod.Get, content, moderator.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement history = await Api.ExpectAsync(f, HttpMethod.Get, content + "?authorUserId=" + poster.Id, moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(4L, history.GetProperty("totalRecords").GetInt64(), "Posts and comments in every state");
            Dictionary<string, string> states = history.GetProperty("objects").EnumerateArray().ToDictionary(o => Api.Str(o, "targetId"), o => Api.Str(o, "state"));
            Check.Equal("Hidden", states[p1], "Auto-hidden post");
            Check.Equal("Removed", states[p2], "Removed post");
            Check.Equal("Deleted", states[p3], "Deleted post");
            Check.Equal("Hidden", states[c1], "Hidden comment");
            JsonElement item = history.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "targetId") == p1);
            Check.Equal(1L, item.GetProperty("reportCount").GetInt64(), "Report count");
            ContentTestKit.CheckMetadata(item, "Content item");
            JsonElement deleted = await Api.ExpectAsync(f, HttpMethod.Get, content + "?authorUserId=" + poster.Id + "&state=Deleted", moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ModerationTestKit.TargetIds(deleted).SequenceEqual(new[] { p3 }), "state filter");
            JsonElement onlyComments = await Api.ExpectAsync(f, HttpMethod.Post, content + "/enumerate", moderator.AccessToken, new { filters = new { authorUserId = poster.Id, targetType = "Comment" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ModerationTestKit.TargetIds(onlyComments).SequenceEqual(new[] { c1 }), "targetType filter");
            JsonElement search = await Api.ExpectAsync(f, HttpMethod.Get, content + "?authorUserId=" + poster.Id + "&search=second", moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ModerationTestKit.TargetIds(search).SequenceEqual(new[] { p2 }), "search");
        }

        private static async Task OverdueAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Moderation.AutoHideReportThreshold = 0;
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember moderator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await ContentTestKit.MemberAsync(f, ct, "Plain").ConfigureAwait(false);
            List<TestMember> reporters = await ModerationTestKit.MembersAsync(f, 2, ct).ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Overdue post", ct).ConfigureAwait(false);
            string commentId = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, postId, "Overdue comment", null, ct).ConfigureAwait(false);
            foreach (TestMember r in reporters) await ModerationTestKit.ReportOkAsync(f, r.AccessToken, "Comment", commentId, ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporters[0].AccessToken, "FeedPost", postId, ct).ConfigureAwait(false);

            DateTime start = DateTime.UtcNow;
            f.Services.Moderation.Clock = () => start;
            ModerationDueWorker worker = new ModerationDueWorker(f.Database, f.Services.Moderation, f.Services.Logger);
            await using (worker.ConfigureAwait(false))
            {
                Check.Equal(0, await worker.RunOnceAsync(ct).ConfigureAwait(false), "Nothing overdue yet");
                JsonElement summary = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/moderation/summary", moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal(3L, summary.GetProperty("open").GetInt64(), "Open reports");
                Check.Equal(0L, summary.GetProperty("overdue").GetInt64(), "None overdue");
                Check.Equal(2L, summary.GetProperty("openTargets").GetInt64(), "Open targets");

                f.Services.Moderation.Clock = () => start.AddHours(f.Settings.Moderation.ReviewTargetHours + 1);
                Check.Equal(3, await worker.RunOnceAsync(ct).ConfigureAwait(false), "Three reports claimed");
                Check.Equal(0, await worker.RunOnceAsync(ct).ConfigureAwait(false), "Each report notifies once");
                summary = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/moderation/summary", moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal(3L, summary.GetProperty("overdue").GetInt64(), "Three overdue");
            }
            foreach (string recipient in new[] { moderator.Id, admin.Id })
            {
                List<Notification> notes = (await NotificationTestKit.ListAsync(f, recipient, ct).ConfigureAwait(false)).Where(n => n.Type == NotificationTypeEnum.ModerationOverdue).ToList();
                Check.Equal(2, notes.Count, "One notice per reported target for " + recipient);
                Notification comment = notes.Single(n => n.TargetType == "Comment");
                Check.Equal("A report on a comment is past the " + f.Settings.Moderation.ReviewTargetHours + " hour review target", comment.Title, "Title");
                Check.Equal(PushStateEnum.Pending, comment.PushState, "Push queued");
            }
            Check.Equal(0L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM notifications WHERE type = 'ModerationOverdue' AND recipientuserid IN ('" + member.Id + "', '" + poster.Id + "');", ct).ConfigureAwait(false), "Non-moderators get nothing");
            Check.Equal(0L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM reports WHERE overduenotifiedutc IS NULL;", ct).ConfigureAwait(false), "Every report marked notified");
        }

        private static async Task ModeratorsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember candidate = await ContentTestKit.MemberAsync(f, ct, "Candidate").ConfigureAwait(false);
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "to moderate", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, ModerationTestKit.Queue, candidate.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);

            JsonElement granted = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderators", admin.AccessToken, new { userId = candidate.Id, resourceType = "Feed" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(candidate.Id, Api.Str(granted.GetProperty("user"), "id"), "Row user");
            Check.Equal("FeedModerator", Api.Str(granted.GetProperty("roles")[0], "role"), "FeedModerator role");
            Check.Equal("Feed", Api.Str(granted.GetProperty("roles")[0], "resourceType"), "Feed scope");
            Check.Equal("Admin Person", Api.Str(granted, "grantedByName"), "Granted by");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderators", admin.AccessToken, new { userId = candidate.Id }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM roleassignments WHERE userid = '" + candidate.Id + "' AND role = 'FeedModerator';", ct).ConfigureAwait(false), "Idempotent grant");
            Check.Equal(1L, await ModerationTestKit.AuditCountAsync(f, "member.roles", candidate.Id, ct).ConfigureAwait(false), "Grant audited once");
            await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue, candidate.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, candidate.AccessToken, "FeedPost", postId, "Hide", ct).ConfigureAwait(false);

            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/moderators", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement row = list.GetProperty("objects").EnumerateArray().Single(o => Api.Str(o.GetProperty("user"), "id") == candidate.Id);
            Check.Equal(1L, row.GetProperty("actions30d").GetInt64(), "Actions in 30 days");
            Check.True(row.TryGetProperty("medianResponseMinutes30d", out JsonElement _) && row.TryGetProperty("overdueHandled30d", out JsonElement _) && row.TryGetProperty("openInScope", out JsonElement _), "Statistics fields");
            ContentTestKit.CheckMetadata(row, "Moderator row");
            Check.True(!list.GetProperty("objects").EnumerateArray().Any(o => Api.Str(o.GetProperty("user"), "id") == admin.Id), "SystemAdmins without a moderator role are not listed");
            JsonElement filtered = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderators/enumerate", admin.AccessToken, new { filters = new { role = "Moderator" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, filtered.GetProperty("objects").GetArrayLength(), "role filter");

            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/moderators", candidate.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/moderators", admin.AccessToken, new { userId = candidate.Id, resourceType = "Global" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/moderators", admin.AccessToken, new { userId = "usr_missing" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);

            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/moderators/" + candidate.Id + "?resourceType=Feed", admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, ModerationTestKit.Queue, candidate.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/moderators/" + candidate.Id, admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.Equal(2L, await ModerationTestKit.AuditCountAsync(f, "member.roles", candidate.Id, ct).ConfigureAwait(false), "Revoke audited once");
            JsonElement log = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Actions + "?moderatorUserId=" + candidate.Id, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, log.GetProperty("totalRecords").GetInt64(), "Past actions stay attributed");
        }

        private static async Task PurgeAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Moderation.AutoHideReportThreshold = 0;
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember moderator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            TestMember reporter = await ContentTestKit.MemberAsync(f, ct, "Reporter").ConfigureAwait(false);
            string mediaId = await ContentTestKit.MediaAsync(f, poster.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            string removed = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "removed body", mediaIds = new[] { mediaId } }, ct).ConfigureAwait(false), "id");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + removed, poster.AccessToken, new { body = "removed edit" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE feedposts SET publishedutc = '2000-01-01T00:00:00.000Z' WHERE id = '" + removed + "';", false, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + removed, poster.AccessToken, new { body = "removed edit two", labels = new[] { "keep" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string held = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "held by a report", ct).ConfigureAwait(false);
            string kept = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "still visible", ct).ConfigureAwait(false);
            string comment = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, kept, "deleted comment", null, ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "FeedPost", removed, "Remove", ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "FeedPost", held, "Remove", ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "User", poster.Id, ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE reports SET targettype = 'FeedPost', targetid = '" + held + "' WHERE reporteruserid = '" + reporter.Id + "';", false, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/comments/" + comment, poster.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);

            DateTime now = DateTime.UtcNow;
            ContentPurgeWorker worker = new ContentPurgeWorker(f.Database, f.Services.Moderation, f.Services.Logger);
            await using (worker.ConfigureAwait(false))
            {
                f.Services.Moderation.Clock = () => now.AddDays(f.Settings.Moderation.RetainRemovedDays - 1);
                Check.Equal(0, await worker.RunOnceAsync(ct).ConfigureAwait(false), "Nothing purged inside the retention period");
                f.Services.Moderation.Clock = () => now.AddDays(f.Settings.Moderation.RetainRemovedDays + 1);
                Check.Equal(2, await worker.RunOnceAsync(ct).ConfigureAwait(false), "The removed post and the deleted comment are purged");
                Check.Equal(0, await worker.RunOnceAsync(ct).ConfigureAwait(false), "Idempotent");
            }
            FeedPost purged = (await f.Database.FeedPosts.ReadAsync(removed, ct).ConfigureAwait(false))!;
            Check.True(purged.PurgedUtc != null && purged.Body.Length == 0 && purged.Excerpt.Length == 0 && purged.RevisionCount == 0, "Body and revisions cleared");
            Check.Equal(0L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM feedpostrevisions WHERE feedpostid = '" + removed + "';", ct).ConfigureAwait(false), "Revisions deleted");
            Check.Equal(0L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM labels WHERE targetid = '" + removed + "';", ct).ConfigureAwait(false), "Labels deleted");
            Check.True((await f.Database.Media.ReadAsync(mediaId, ct).ConfigureAwait(false))?.DeletedUtc != null, "Media deleted");
            Check.True((await f.Database.FeedPosts.ReadAsync(held, ct).ConfigureAwait(false))!.PurgedUtc == null, "An Open report keeps content");
            Check.True((await f.Database.FeedPosts.ReadAsync(kept, ct).ConfigureAwait(false))!.PurgedUtc == null, "Visible content is never purged");
            Check.True((await f.Database.Comments.ReadAsync(comment, ct).ConfigureAwait(false))!.PurgedUtc != null, "Deleted comment purged");
            ApiResult restore = await ModerationTestKit.ActAsync(f, moderator.AccessToken, "FeedPost", removed, "Restore", ct).ConfigureAwait(false);
            Check.Equal("InvalidModerationState", restore.Error, "Purged content cannot be restored");
        }

        private static async Task AccountDeletionAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Moderation.AutoHideReportThreshold = 0;
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember leaving = await ContentTestKit.PosterAsync(f, ct, "Leaving Member").ConfigureAwait(false);
            TestMember moderator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            string other = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "someone else's post", ct).ConfigureAwait(false);
            string mine = await ContentTestKit.PostIdAsync(f, leaving.AccessToken, "my post", ct).ConfigureAwait(false);
            string comment = await ContentTestKit.CommentIdAsync(f, leaving.AccessToken, other, "my comment", null, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + other + "/reactions/like", leaving.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string unattached = await ContentTestKit.MediaAsync(f, leaving.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            string reportId = Api.Str(await ModerationTestKit.ReportOkAsync(f, leaving.AccessToken, "FeedPost", other, ct).ConfigureAwait(false), "id");

            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", leaving.AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
            Comment erased = (await f.Database.Comments.ReadAsync(comment, ct).ConfigureAwait(false))!;
            Check.True(erased.DeletedUtc != null && erased.Body == null && erased.DeletedReason == CommentDeletedReasonEnum.AccountDeleted, "Comment erased");
            Check.True((await f.Database.FeedPosts.ReadAsync(mine, ct).ConfigureAwait(false))!.DeletedUtc != null, "Post soft-deleted");
            Check.Equal(0L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM reactions WHERE userid = '" + leaving.Id + "';", ct).ConfigureAwait(false), "Reactions deleted");
            Check.True((await f.Database.Media.ReadAsync(unattached, ct).ConfigureAwait(false))?.DeletedUtc != null, "Unattached media deleted");
            Report kept = (await f.Database.Reports.ReadAsync(reportId, ct).ConfigureAwait(false))!;
            Check.Equal(ReportStatusEnum.Open, kept.Status, "Filed report kept");
            JsonElement reports = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "/FeedPost/" + other + "/reports", moderator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement reporter = reports.GetProperty("objects")[0].GetProperty("reporter");
            Check.True(reporter.GetProperty("isDeleted").GetBoolean(), "Reporter shown as Deleted member");
            Check.Equal("Deleted member", Api.Str(reporter, "displayName"), "Deleted member name");
        }

        private static async Task PreviewAndSearchAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct, "Searchable Poster", "searchme").ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "preview", ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, admin.AccessToken, "FeedPost", postId, "Hide", ct, "Check").ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, admin.AccessToken, "FeedPost", postId, "Unhide", ct, "Fine").ConfigureAwait(false);
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/feed/" + postId, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement preview = detail.GetProperty("moderationActions");
            Check.Equal(2L, preview.GetProperty("totalRecords").GetInt64(), "Two actions");
            Check.Equal("Unhide", Api.Str(preview.GetProperty("objects")[0], "action"), "Newest first");
            Check.Equal("/api/v1/admin/moderation/queue/FeedPost/" + postId + "/actions/enumerate", Api.Str(preview, "enumerate"), "Enumerate path");

            TestMember searcher = await ContentTestKit.MemberAsync(f, ct, "Searcher").ConfigureAwait(false);
            JsonElement result = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/members/search?q=sea&limit=3", searcher.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(3, result.GetProperty("maxResults").GetInt32(), "limit is an alias of maxResults");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/members/search?q=sea&limit=x", searcher.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        private static async Task ExportAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember member = await ContentTestKit.PosterAsync(f, ct, "Exporter").ConfigureAwait(false);
            string other = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "other post", ct).ConfigureAwait(false);
            string mine = await ContentTestKit.PostIdAsync(f, member.AccessToken, "my post", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + mine, member.AccessToken, new { labels = new[] { "mine" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ContentTestKit.CommentIdAsync(f, member.AccessToken, other, "my comment", null, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/feed/" + other + "/reactions/like", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/users/" + poster.Id + "/block", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult filed = await ModerationTestKit.ReportAsync(f, member.AccessToken, "FeedPost", other, ct, "Other", "Details of the report").ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Created, filed.Status, "Report filed (body: " + filed.Text + ")");
            string mediaId = await ContentTestKit.MediaAsync(f, member.Id, MediaKindEnum.Image, ct, altText: "alt text").ConfigureAwait(false);
            string pushToken = NotificationTestKit.NewToken();
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/push-tokens", member.AccessToken, new { token = pushToken, platform = "Ios", deviceName = "Phone", appVersion = "1.0.0" }, HttpStatusCode.Created, ct).ConfigureAwait(false);

            JsonElement file = await AccountExportSuite.ExportAsync(f, member, ct).ConfigureAwait(false);
            foreach (string section in new[] { "feedPosts", "comments", "reactions", "notificationPreferences", "pushTokens", "blocks", "reports", "media" })
                Check.True(file.TryGetProperty(section, out JsonElement _), "Section " + section);
            Check.Equal(mine, Api.Str(file.GetProperty("feedPosts")[0], "id"), "Own post");
            Check.Equal("my comment", Api.Str(file.GetProperty("comments")[0], "body"), "Own comment");
            Check.Equal("like", Api.Str(file.GetProperty("reactions")[0], "key"), "Own reaction");
            Check.Equal(JsonValueKind.Object, file.GetProperty("notificationPreferences").ValueKind, "Preferences document");
            Check.Equal(1, file.GetProperty("pushTokens").GetArrayLength(), "Push token");
            Check.Equal("Phone", Api.Str(file.GetProperty("pushTokens")[0], "deviceName"), "Device name");
            Check.True(!file.GetRawText().Contains(pushToken, StringComparison.Ordinal), "No token values");
            Check.Equal(poster.Id, Api.Str(file.GetProperty("blocks")[0].GetProperty("user"), "id"), "Blocked member");
            Check.Equal("Details of the report", Api.Str(file.GetProperty("reports")[0], "details"), "Filed report");
            JsonElement media = file.GetProperty("media")[0];
            Check.Equal(mediaId, Api.Str(media, "id"), "Media metadata");
            Check.True(!media.TryGetProperty("sourceKey", out JsonElement _), "No storage keys");
            Check.True(file.GetProperty("metadata").EnumerateArray().Any(m => Api.Str(m, "targetId") == mine && m.GetProperty("labels").EnumerateArray().Any(l => l.GetString() == "mine")), "Post labels in the metadata section");
        }

        #endregion
    }
}
