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
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Phase 2 moderation (contract sections 9.1 and 9.3): reports (validation, visibility, duplicates, self, rate limit,
    /// muted and restricted reporters), auto-hide (threshold, concurrency, never for SystemAdmin content, Filter reports
    /// not counting, disabled), and moderation actions (scope and staff-content protection, transitions, notify
    /// defaults, resolveReports, media retained and restored, audit).
    /// </summary>
    public static class ModerationSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Moderation";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Moderation (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Routes.Groups", "Reports are Member routes, moderation routes are Moderator, word filter and moderators are Admin", ct =>
                {
                    Check.Equal(RouteGroupEnum.Member, AuthorizationService.Classify("/api/v1/reports"), "Reports");
                    Check.Equal(RouteGroupEnum.Moderator, AuthorizationService.Classify("/api/v1/admin/moderation/queue"), "Queue");
                    Check.Equal(RouteGroupEnum.Moderator, AuthorizationService.Classify("/api/v1/admin/moderation/actions"), "Actions");
                    Check.Equal(RouteGroupEnum.Admin, AuthorizationService.Classify("/api/v1/admin/moderators"), "Moderators");
                    return Task.CompletedTask;
                }),
                Case("Reports.Create", "Reports validate target, reason, and details, return 201 then 200 for the caller's Open duplicate, refuse self reports, hide invisible targets, and carry labels and tags", ReportsCreateAsync),
                Case("Reports.Reporters", "Muted members and members without canPost may report; the admin token cannot; reports are rate limited with Retry-After", ReportsReportersAsync),
                Case("AutoHide.Threshold", "Three distinct reporters hide a post with one system AutoHide action; the author is not notified; fewer do not; Filter reports do not count", AutoHideThresholdAsync),
                Case("AutoHide.Race", "Concurrent reports from three members hide a comment and a post exactly once and keep counts consistent", AutoHideRaceAsync),
                Case("AutoHide.NeverAdmin", "Content by a SystemAdmin is never auto-hidden, and threshold 0 disables auto-hide", AutoHideNeverAdminAsync),
                Case("Actions.Scope", "FeedModerators act on member content but get 403 on SystemAdmin content; SystemAdmin acts on anything; members get 403; invalid transitions are 409", ActionsScopeAsync),
                Case("Actions.Notify", "notifyAuthor defaults to true for Remove and Restore and false for Hide and Unhide; the notification names the action and reason", ActionsNotifyAsync),
                Case("Actions.ResolveReports", "Actions resolve Open reports as ActionTaken with the action id by default, resolveReports false keeps them Open, and openReportCount follows", ActionsResolveAsync),
                Case("Actions.MediaAndCounts", "Remove retains served media and Restore brings it back; comment counts update on Hide and Unhide; actions are audited", ActionsMediaAsync),
                Case("Regression.Moderation.BreakGlassActor", "Moderation by the break-glass token records the actor as \"break-glass token\", distinct from system AutoHide, in the actions log and the audit record", async (f, ct) =>
                {
                    f.Settings.Moderation.AutoHideReportThreshold = 1;
                    TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
                    TestMember reporter = await ContentTestKit.MemberAsync(f, ct, "Reporter").ConfigureAwait(false);
                    string byToken = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Token target", ct).ConfigureAwait(false);
                    string bySystem = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "System target", ct).ConfigureAwait(false);
                    await ModerationTestKit.ActOkAsync(f, HttpFixture.AdminToken, "FeedPost", byToken, "Hide", ct).ConfigureAwait(false);
                    await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "FeedPost", bySystem, ct).ConfigureAwait(false);
                    JsonElement log = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Actions, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    JsonElement token = log.GetProperty("objects").EnumerateArray().Single(o => Api.Str(o, "targetId") == byToken);
                    Check.Equal("break-glass token", Api.Str(token, "moderatorName"), "Break-glass actor name");
                    Check.True(token.GetProperty("moderator").ValueKind == JsonValueKind.Null, "No member moderator");
                    JsonElement system = log.GetProperty("objects").EnumerateArray().Single(o => Api.Str(o, "targetId") == bySystem);
                    Check.Equal("AutoHide", Api.Str(system, "action"), "System action");
                    Check.Equal("system", Api.Str(system, "moderatorName"), "System actor name");
                    JsonElement systemOnly = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Actions + "?moderatorUserId=system", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(ModerationTestKit.TargetIds(systemOnly).SequenceEqual(new[] { bySystem }), "The system filter excludes break-glass actions");
                    JsonElement audit = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/audit?action=moderation.hide", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("break-glass token", Api.Str(audit.GetProperty("objects")[0], "actorName"), "Audit actor name");
                }),
                Case("Metadata.OnCreate", "Report create, moderation action create, and dismiss accept labels and tags; members cannot set staff: entries; invalid metadata is 400 before anything is written", MetadataOnCreateAsync)
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

        private static async Task ReportsCreateAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember reporter = await ContentTestKit.MemberAsync(f, ct, "Reporter").ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "A post to report", ct).ConfigureAwait(false);

            JsonElement created = await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "FeedPost", postId, ct).ConfigureAwait(false);
            string reportId = Api.Str(created, "id");
            Check.True(reportId.StartsWith("rpt_", StringComparison.Ordinal), "Report id prefix");
            Check.Equal("Open", Api.Str(created, "status"), "Open");
            ContentTestKit.CheckMetadata(created, "Report");
            JsonElement again = await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "FeedPost", postId, ct, "Harassment", HttpStatusCode.OK).ConfigureAwait(false);
            Check.Equal(reportId, Api.Str(again, "id"), "Duplicate returns the existing Open report");
            Check.Equal(1L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM reports WHERE targetid = '" + postId + "';", ct).ConfigureAwait(false), "No duplicate row");
            FeedPost stored = (await f.Database.FeedPosts.ReadAsync(postId, ct).ConfigureAwait(false))!;
            Check.Equal(1, stored.OpenReportCount, "openReportCount recomputed");
            Report row = (await f.Database.Reports.ReadAsync(reportId, ct).ConfigureAwait(false))!;
            Check.Equal(poster.Id, row.TargetAuthorUserId, "Target author recorded");
            Check.Equal("Feed", row.ScopeType, "Feed scope");
            Check.True(Math.Abs((row.DueUtc - row.CreatedUtc).TotalHours - f.Settings.Moderation.ReviewTargetHours) < 0.01, "Due after the review target");

            ApiResult self = await ModerationTestKit.ReportAsync(f, poster.AccessToken, "FeedPost", postId, ct).ConfigureAwait(false);
            Check.Equal("CannotReportSelf", self.Error, "Own post");
            ApiResult selfUser = await ModerationTestKit.ReportAsync(f, reporter.AccessToken, "User", reporter.Id, ct).ConfigureAwait(false);
            Check.Equal("CannotReportSelf", selfUser.Error, "Own profile");
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "User", poster.Id, ct, "Impersonation").ConfigureAwait(false);

            ApiResult filter = await ModerationTestKit.ReportAsync(f, reporter.AccessToken, "Comment", "cmt_x", ct, "Filter").ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, filter.Status, "Filter is not a member reason");
            Check.True(filter.Body.GetProperty("fieldErrors").TryGetProperty("reason", out JsonElement _), "fieldErrors.reason");
            ApiResult badType = await ModerationTestKit.ReportAsync(f, reporter.AccessToken, "Media", "med_x", ct).ConfigureAwait(false);
            Check.True(badType.Body.GetProperty("fieldErrors").TryGetProperty("targetType", out JsonElement _), "fieldErrors.targetType");
            ApiResult other = await ModerationTestKit.ReportAsync(f, reporter.AccessToken, "FeedPost", postId, ct, "Other", "short").ConfigureAwait(false);
            Check.True(other.Body.GetProperty("fieldErrors").TryGetProperty("details", out JsonElement _), "Other needs 10 characters of details");
            ApiResult longDetails = await ModerationTestKit.ReportAsync(f, reporter.AccessToken, "FeedPost", postId, ct, "Spam", new string('x', 1001)).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, longDetails.Status, "Details at most 1000");

            ApiResult missing = await ModerationTestKit.ReportAsync(f, reporter.AccessToken, "FeedPost", "fpo_missing", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.NotFound, missing.Status, "Unknown post");
            string commentId = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, postId, "a comment", null, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, admin.AccessToken, "Comment", commentId, "Hide", ct).ConfigureAwait(false);
            ApiResult hidden = await ModerationTestKit.ReportAsync(f, reporter.AccessToken, "Comment", commentId, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.NotFound, hidden.Status, "A hidden comment is not visible to the reporter");
            string scheduled = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "later", publishAtUtc = DateTime.UtcNow.AddDays(2) }, ct).ConfigureAwait(false), "id");
            ApiResult unpublished = await ModerationTestKit.ReportAsync(f, reporter.AccessToken, "FeedPost", scheduled, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.NotFound, unpublished.Status, "A scheduled post is not visible to other members");
            TestMember deleted = await ContentTestKit.MemberAsync(f, ct, "Gone").ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", deleted.AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
            ApiResult goneUser = await ModerationTestKit.ReportAsync(f, reporter.AccessToken, "User", deleted.Id, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.NotFound, goneUser.Status, "A deleted member cannot be reported");
        }

        private static async Task ReportsReportersAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Report me", ct).ConfigureAwait(false);
            TestMember muted = await ContentTestKit.MemberAsync(f, ct, "Muted").ConfigureAwait(false);
            await SafetyHelpers.UpdateUserAsync(f, muted.Id, u => { u.IsMuted = true; u.MutedUntilUtc = DateTime.UtcNow.AddDays(1); }, ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, muted.AccessToken, "FeedPost", postId, ct).ConfigureAwait(false);
            TestMember restricted = await ContentTestKit.MemberAsync(f, ct, "Restricted").ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/members/" + restricted.Id + "/flags", HttpFixture.AdminToken, new { canPost = false, canPostMedia = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, restricted.AccessToken, "FeedPost", postId, ct).ConfigureAwait(false);
            ApiResult token = await ModerationTestKit.ReportAsync(f, HttpFixture.AdminToken, "FeedPost", postId, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, token.Status, "The admin token cannot report");

            f.Settings.Moderation.ReportsPerHour = 2;
            TestMember busy = await ContentTestKit.MemberAsync(f, ct, "Busy").ConfigureAwait(false);
            List<string> posts = new List<string>();
            for (int i = 0; i < 3; i++) posts.Add(await ContentTestKit.PostIdAsync(f, poster.AccessToken, "post " + i, ct).ConfigureAwait(false));
            await ModerationTestKit.ReportOkAsync(f, busy.AccessToken, "FeedPost", posts[0], ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, busy.AccessToken, "FeedPost", posts[1], ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, busy.AccessToken, "FeedPost", posts[1], ct, "Spam", HttpStatusCode.OK).ConfigureAwait(false);
            ApiResult limited = await ModerationTestKit.ReportAsync(f, busy.AccessToken, "FeedPost", posts[2], ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.TooManyRequests, limited.Status, "Third new report in an hour");
            Check.True(!String.IsNullOrEmpty(limited.RetryAfter), "Retry-After sent");
        }

        private static async Task AutoHideThresholdAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Controversial", ct).ConfigureAwait(false);
            List<TestMember> reporters = await ModerationTestKit.MembersAsync(f, 3, ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporters[0].AccessToken, "FeedPost", postId, ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporters[0].AccessToken, "FeedPost", postId, ct, "Spam", HttpStatusCode.OK).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporters[1].AccessToken, "FeedPost", postId, ct).ConfigureAwait(false);
            Check.Equal("None", await ModerationTestKit.StateAsync(f, "feedposts", postId, ct).ConfigureAwait(false), "Two reporters do not hide");
            await ModerationTestKit.ReportOkAsync(f, reporters[2].AccessToken, "FeedPost", postId, ct).ConfigureAwait(false);
            Check.Equal("Hidden", await ModerationTestKit.StateAsync(f, "feedposts", postId, ct).ConfigureAwait(false), "Three reporters hide");
            Check.Equal(1L, await ModerationTestKit.AutoHideCountAsync(f, postId, ct).ConfigureAwait(false), "One AutoHide action");
            Check.Equal(1L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM moderationactions WHERE targetid = '" + postId + "' AND moderatoruserid IS NULL;", ct).ConfigureAwait(false), "System action without a moderator");
            Check.Equal(0L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM notifications WHERE recipientuserid = '" + poster.Id + "' AND type = 'ModerationAction';", ct).ConfigureAwait(false), "Author not notified");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed/" + postId, reporters[0].AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + postId, poster.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);

            string filtered = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Filtered too", ct).ConfigureAwait(false);
            await f.Database.WordFilter.CreateSystemReportAsync(IdGenerator.Generate(Constants.ReportIdPrefix), TargetTypeEnum.FeedPost, filtered, poster.Id, "Word filter", DateTime.UtcNow, DateTime.UtcNow.AddHours(12), ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporters[0].AccessToken, "FeedPost", filtered, ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporters[1].AccessToken, "FeedPost", filtered, ct).ConfigureAwait(false);
            Check.Equal("None", await ModerationTestKit.StateAsync(f, "feedposts", filtered, ct).ConfigureAwait(false), "A Filter report does not count toward auto-hide");
            Check.Equal(3L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM reports WHERE targetid = '" + filtered + "' AND status = 'Open';", ct).ConfigureAwait(false), "Three Open reports");

            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, admin.AccessToken, "FeedPost", postId, "Unhide", ct, "Looks fine").ConfigureAwait(false);
            Check.Equal("None", await ModerationTestKit.StateAsync(f, "feedposts", postId, ct).ConfigureAwait(false), "Unhidden");
            List<TestMember> more = await ModerationTestKit.MembersAsync(f, 3, ct, "Second wave").ConfigureAwait(false);
            foreach (TestMember m in more) await ModerationTestKit.ReportOkAsync(f, m.AccessToken, "FeedPost", postId, ct).ConfigureAwait(false);
            Check.Equal("Hidden", await ModerationTestKit.StateAsync(f, "feedposts", postId, ct).ConfigureAwait(false), "A new set of reporters hides it again after Unhide");
            Check.Equal(2L, await ModerationTestKit.AutoHideCountAsync(f, postId, ct).ConfigureAwait(false), "A second AutoHide action");
        }

        private static async Task AutoHideRaceAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Racy post", ct).ConfigureAwait(false);
            string parent = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, postId, "parent", null, ct).ConfigureAwait(false);
            string commentId = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, postId, "reply", parent, ct).ConfigureAwait(false);
            for (int round = 0; round < 2; round++)
            {
                List<TestMember> reporters = await ModerationTestKit.MembersAsync(f, 4, ct, "Racer " + round).ConfigureAwait(false);
                string target = round == 0 ? commentId : postId;
                string type = round == 0 ? "Comment" : "FeedPost";
                ApiResult[] results = await Task.WhenAll(reporters.Select(r => ModerationTestKit.ReportAsync(f, r.AccessToken, type, target, ct))).ConfigureAwait(false);
                Check.True(results.All(r => r.Status == HttpStatusCode.Created), type + ": every report created");
                Check.Equal(1L, await ModerationTestKit.AutoHideCountAsync(f, target, ct).ConfigureAwait(false), type + ": exactly one AutoHide action");
                Check.Equal("Hidden", await ModerationTestKit.StateAsync(f, round == 0 ? "comments" : "feedposts", target, ct).ConfigureAwait(false), type + ": hidden");
            }
            await ContentTestKit.VerifyCountsAsync(f, postId, ct).ConfigureAwait(false);
            Comment parentRow = (await f.Database.Comments.ReadAsync(parent, ct).ConfigureAwait(false))!;
            Check.Equal(0, parentRow.ChildCount, "The hidden reply no longer counts");
        }

        private static async Task AutoHideNeverAdminAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            string adminPost = await ContentTestKit.PostIdAsync(f, admin.AccessToken, "Official post", ct).ConfigureAwait(false);
            string adminComment = await ContentTestKit.CommentIdAsync(f, admin.AccessToken, adminPost, "Official comment", null, ct).ConfigureAwait(false);
            List<TestMember> reporters = await ModerationTestKit.MembersAsync(f, 3, ct).ConfigureAwait(false);
            foreach (TestMember r in reporters)
            {
                await ModerationTestKit.ReportOkAsync(f, r.AccessToken, "FeedPost", adminPost, ct).ConfigureAwait(false);
                await ModerationTestKit.ReportOkAsync(f, r.AccessToken, "Comment", adminComment, ct).ConfigureAwait(false);
            }
            Check.Equal("None", await ModerationTestKit.StateAsync(f, "feedposts", adminPost, ct).ConfigureAwait(false), "Admin post stays visible");
            Check.Equal("None", await ModerationTestKit.StateAsync(f, "comments", adminComment, ct).ConfigureAwait(false), "Admin comment stays visible");

            f.Settings.Moderation.AutoHideReportThreshold = 0;
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Member post", ct).ConfigureAwait(false);
            foreach (TestMember r in reporters) await ModerationTestKit.ReportOkAsync(f, r.AccessToken, "FeedPost", postId, ct).ConfigureAwait(false);
            Check.Equal("None", await ModerationTestKit.StateAsync(f, "feedposts", postId, ct).ConfigureAwait(false), "Threshold 0 disables auto-hide");
            f.Settings.Moderation.AutoHideReportThreshold = 1;
            string single = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "One report", ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporters[0].AccessToken, "FeedPost", single, ct).ConfigureAwait(false);
            Check.Equal("Hidden", await ModerationTestKit.StateAsync(f, "feedposts", single, ct).ConfigureAwait(false), "Threshold 1 hides on the first report");
        }

        private static async Task ActionsScopeAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember moderator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            TestMember member = await ContentTestKit.MemberAsync(f, ct, "Plain").ConfigureAwait(false);
            string memberPost = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Member post", ct).ConfigureAwait(false);
            string memberComment = await ContentTestKit.CommentIdAsync(f, member.AccessToken, memberPost, "Member comment", null, ct).ConfigureAwait(false);
            string adminPost = await ContentTestKit.PostIdAsync(f, admin.AccessToken, "Admin post", ct).ConfigureAwait(false);
            string adminComment = await ContentTestKit.CommentIdAsync(f, admin.AccessToken, memberPost, "Admin comment", null, ct).ConfigureAwait(false);

            JsonElement hidden = await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "FeedPost", memberPost, "Hide", ct, "Off topic").ConfigureAwait(false);
            JsonElement action = hidden.GetProperty("action");
            Check.True(Api.Str(action, "id").StartsWith("mac_", StringComparison.Ordinal), "Action id prefix");
            Check.Equal("Hide", Api.Str(action, "action"), "Action");
            Check.Equal(moderator.Id, Api.Str(action.GetProperty("moderator"), "id"), "Moderator");
            Check.Equal(poster.Id, Api.Str(action.GetProperty("targetAuthor"), "id"), "Target author");
            ContentTestKit.CheckMetadata(action, "Action");
            Check.Equal("Hidden", Api.Str(hidden.GetProperty("target").GetProperty("moderation"), "state"), "Target returned as moderators see it");
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "Comment", memberComment, "Remove", ct).ConfigureAwait(false);

            ApiResult adminContent = await ModerationTestKit.ActAsync(f, moderator.AccessToken, "FeedPost", adminPost, "Hide", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, adminContent.Status, "FeedModerator on a SystemAdmin post");
            ApiResult adminReply = await ModerationTestKit.ActAsync(f, moderator.AccessToken, "Comment", adminComment, "Remove", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, adminReply.Status, "FeedModerator on a SystemAdmin comment");
            await ModerationTestKit.ActOkAsync(f, admin.AccessToken, "FeedPost", adminPost, "Hide", ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, HttpFixture.AdminToken, "FeedPost", adminPost, "Unhide", ct).ConfigureAwait(false);
            ApiResult plain = await ModerationTestKit.ActAsync(f, member.AccessToken, "FeedPost", memberPost, "Unhide", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, plain.Status, "Members are not moderators");

            ApiResult twice = await ModerationTestKit.ActAsync(f, moderator.AccessToken, "FeedPost", memberPost, "Hide", ct).ConfigureAwait(false);
            Check.Equal("InvalidModerationState", twice.Error, "Already hidden");
            ApiResult restore = await ModerationTestKit.ActAsync(f, moderator.AccessToken, "FeedPost", memberPost, "Restore", ct).ConfigureAwait(false);
            Check.Equal("InvalidModerationState", restore.Error, "Restore needs Removed");
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "FeedPost", memberPost, "Remove", ct).ConfigureAwait(false);
            ApiResult hideRemoved = await ModerationTestKit.ActAsync(f, moderator.AccessToken, "FeedPost", memberPost, "Hide", ct).ConfigureAwait(false);
            Check.Equal("InvalidModerationState", hideRemoved.Error, "Removed content cannot be hidden");
            ApiResult unhide = await ModerationTestKit.ActAsync(f, moderator.AccessToken, "Comment", memberComment, "Unhide", ct).ConfigureAwait(false);
            Check.Equal("InvalidModerationState", unhide.Error, "Unhide needs Hidden");
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "FeedPost", memberPost, "Restore", ct).ConfigureAwait(false);
            Check.Equal("None", await ModerationTestKit.StateAsync(f, "feedposts", memberPost, ct).ConfigureAwait(false), "Restored");

            ApiResult shortReason = await ModerationTestKit.ActAsync(f, moderator.AccessToken, "FeedPost", memberPost, "Hide", ct, "no").ConfigureAwait(false);
            Check.True(shortReason.Body.GetProperty("fieldErrors").TryGetProperty("reason", out JsonElement _), "Reason 3 to 500");
            ApiResult badAction = await ModerationTestKit.ActAsync(f, moderator.AccessToken, "FeedPost", memberPost, "AutoHide", ct).ConfigureAwait(false);
            Check.True(badAction.Body.GetProperty("fieldErrors").TryGetProperty("action", out JsonElement _), "AutoHide is not a moderator action");
            ApiResult badType = await ModerationTestKit.ActAsync(f, moderator.AccessToken, "User", poster.Id, "Hide", ct).ConfigureAwait(false);
            Check.True(badType.Body.GetProperty("fieldErrors").TryGetProperty("targetType", out JsonElement _), "Only posts and comments");
            ApiResult missing = await ModerationTestKit.ActAsync(f, moderator.AccessToken, "FeedPost", "fpo_missing", "Hide", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.NotFound, missing.Status, "Unknown target");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/feed/" + memberPost, poster.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            ApiResult deleted = await ModerationTestKit.ActAsync(f, moderator.AccessToken, "FeedPost", memberPost, "Hide", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.NotFound, deleted.Status, "Deleted post");
        }

        private static async Task ActionsNotifyAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember moderator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Notify me", ct).ConfigureAwait(false);
            string commentId = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, postId, "comment", null, ct).ConfigureAwait(false);
            string sql = "SELECT COUNT(*) AS cnt FROM notifications WHERE recipientuserid = '" + poster.Id + "' AND type = 'ModerationAction';";

            JsonElement hide = await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "Comment", commentId, "Hide", ct).ConfigureAwait(false);
            Check.True(!hide.GetProperty("action").GetProperty("notifiedAuthor").GetBoolean(), "Hide does not notify by default");
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "Comment", commentId, "Unhide", ct).ConfigureAwait(false);
            Check.Equal(0L, await ModerationTestKit.CountAsync(f, sql, ct).ConfigureAwait(false), "No notification for Hide and Unhide");
            JsonElement remove = await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "Comment", commentId, "Remove", ct, "Spam").ConfigureAwait(false);
            Check.True(remove.GetProperty("action").GetProperty("notifiedAuthor").GetBoolean(), "Remove notifies by default");
            List<Notification> notes = await NotificationTestKit.ListAsync(f, poster.Id, ct).ConfigureAwait(false);
            Notification removed = notes.Single(n => n.Type == NotificationTypeEnum.ModerationAction);
            Check.Equal("A moderator removed your comment (Spam)", removed.Title, "Title");
            Check.Equal("/feed/" + postId + "?comment=" + commentId, removed.Path, "Path");
            Check.True(removed.ActorUserId == null, "The moderator is not revealed");
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "Comment", commentId, "Restore", ct, "Mistake").ConfigureAwait(false);
            Check.Equal(2L, await ModerationTestKit.CountAsync(f, sql, ct).ConfigureAwait(false), "Restore notifies by default");
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "FeedPost", postId, "Hide", ct, "Check", notifyAuthor: true).ConfigureAwait(false);
            Check.Equal(3L, await ModerationTestKit.CountAsync(f, sql, ct).ConfigureAwait(false), "notifyAuthor true on Hide");
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "FeedPost", postId, "Remove", ct, "Spam", notifyAuthor: false).ConfigureAwait(false);
            Check.Equal(3L, await ModerationTestKit.CountAsync(f, sql, ct).ConfigureAwait(false), "notifyAuthor false on Remove");
        }

        private static async Task ActionsResolveAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember moderator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            List<TestMember> reporters = await ModerationTestKit.MembersAsync(f, 2, ct).ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Resolve me", ct).ConfigureAwait(false);
            foreach (TestMember r in reporters) await ModerationTestKit.ReportOkAsync(f, r.AccessToken, "FeedPost", postId, ct).ConfigureAwait(false);
            Check.Equal(2, (await f.Database.FeedPosts.ReadAsync(postId, ct).ConfigureAwait(false))!.OpenReportCount, "Two open reports");
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "FeedPost", postId, "Hide", ct, "Check", resolveReports: false).ConfigureAwait(false);
            Check.Equal(2L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM reports WHERE targetid = '" + postId + "' AND status = 'Open';", ct).ConfigureAwait(false), "resolveReports false keeps them Open");
            JsonElement removed = await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "FeedPost", postId, "Remove", ct, "Spam").ConfigureAwait(false);
            string actionId = Api.Str(removed.GetProperty("action"), "id");
            Check.Equal(2L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM reports WHERE targetid = '" + postId + "' AND status = 'ActionTaken' AND moderationactionid = '" + actionId +
                "' AND resolvedbyuserid = '" + moderator.Id + "';", ct).ConfigureAwait(false), "Resolved as ActionTaken with the action");
            Check.Equal(0, (await f.Database.FeedPosts.ReadAsync(postId, ct).ConfigureAwait(false))!.OpenReportCount, "openReportCount recomputed");
            await ModerationTestKit.ReportOkAsync(f, reporters[0].AccessToken, "User", poster.Id, ct).ConfigureAwait(false);
            JsonElement fresh = await ModerationTestKit.ReportOkAsync(f, reporters[0].AccessToken, "User", poster.Id, ct, "Spam", HttpStatusCode.OK).ConfigureAwait(false);
            Check.Equal("Open", Api.Str(fresh, "status"), "A User report stays Open");
        }

        private static async Task MetadataOnCreateAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember moderator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            List<TestMember> reporters = await ModerationTestKit.MembersAsync(f, 2, ct).ConfigureAwait(false);
            string postId = await ContentTestKit.PostIdAsync(f, poster.AccessToken, "Metadata on create", ct).ConfigureAwait(false);

            ApiResult invalid = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/reports", reporters[0].AccessToken,
                new { targetType = "FeedPost", targetId = postId, reason = "Spam", labels = new[] { "bad label!" } }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, invalid.Status, "Invalid report label is 400 (body: " + invalid.Text + ")");
            Check.Equal(0L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM reports WHERE targetid = '" + postId + "';", ct).ConfigureAwait(false), "No report row for invalid metadata");

            ApiResult created = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/reports", reporters[0].AccessToken,
                new { targetType = "FeedPost", targetId = postId, reason = "Spam", labels = new[] { "triage", "staff:sneaky" }, tags = new Dictionary<string, string> { { "source", "mobile" }, { "staff:priority", "high" } } }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Created, created.Status, "Report with metadata (body: " + created.Text + ")");
            string reportId = Api.Str(created.Body, "id");
            List<string> reportLabels = created.Body.GetProperty("labels").EnumerateArray().Select(l => l.GetString() ?? String.Empty).ToList();
            Check.True(reportLabels.Contains("triage"), "Report label stored and returned");
            Check.True(!reportLabels.Any(l => l.StartsWith("staff:", StringComparison.OrdinalIgnoreCase)), "A member cannot set a staff: label");
            Check.Equal("mobile", Api.Str(created.Body.GetProperty("tags"), "source"), "Report tag stored and returned");
            Check.True(!created.Body.GetProperty("tags").TryGetProperty("staff:priority", out JsonElement _), "A member cannot set a staff: tag");
            JsonElement filtered = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/queue/FeedPost/" + postId + "/reports/enumerate", moderator.AccessToken,
                new { labels = new[] { "triage" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(filtered.GetProperty("objects").EnumerateArray().Any(o => Api.Str(o, "id") == reportId), "The report enumerates by its create-time label");

            await ModerationTestKit.ReportOkAsync(f, reporters[1].AccessToken, "FeedPost", postId, ct).ConfigureAwait(false);
            ApiResult badAction = await Api.SendAsync(f, HttpMethod.Post, ModerationTestKit.Actions, moderator.AccessToken,
                new { targetType = "FeedPost", targetId = postId, action = "Hide", reason = "Spam", tags = new Dictionary<string, string> { { "bad key!", "x" } } }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, badAction.Status, "Invalid action tag is 400 (body: " + badAction.Text + ")");
            Check.Equal("None", await ModerationTestKit.StateAsync(f, "feedposts", postId, ct).ConfigureAwait(false), "Invalid metadata changes nothing");

            ApiResult acted = await Api.SendAsync(f, HttpMethod.Post, ModerationTestKit.Actions, moderator.AccessToken,
                new { targetType = "FeedPost", targetId = postId, action = "Hide", reason = "Spam", resolveReports = false, labels = new[] { "staff:escalated", "spam-wave" }, tags = new Dictionary<string, string> { { "staff:case", "42" } } }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, acted.Status, "Action with metadata (body: " + acted.Text + ")");
            JsonElement action = acted.Body.GetProperty("action");
            List<string> actionLabels = action.GetProperty("labels").EnumerateArray().Select(l => l.GetString() ?? String.Empty).ToList();
            Check.True(actionLabels.Contains("staff:escalated") && actionLabels.Contains("spam-wave"), "Moderators may set staff and public labels on an action");
            Check.Equal("42", Api.Str(action.GetProperty("tags"), "staff:case"), "Action tag stored and returned");
            JsonElement actions = await Api.ExpectAsync(f, HttpMethod.Post, ModerationTestKit.Actions + "/enumerate", moderator.AccessToken,
                new { tags = new Dictionary<string, string> { { "staff:case", "42" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, actions.GetProperty("objects").GetArrayLength(), "The action enumerates by its create-time tag");

            JsonElement dismissed = await Api.ExpectAsync(f, HttpMethod.Post, ModerationTestKit.Queue + "/FeedPost/" + postId + "/dismiss", moderator.AccessToken,
                new { note = "Not spam", labels = new[] { "false-positive" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(dismissed.ValueKind == JsonValueKind.Object, "Dismiss detail returned");
            Check.Equal(1L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM labels l JOIN moderationactions a ON a.id = l.targetid WHERE l.targettype = 'ModerationAction' AND a.targetid = '" + postId +
                "' AND a.action = 'Dismiss' AND l.labelnormalized = 'false-positive';", ct).ConfigureAwait(false), "Dismiss action carries the label");
        }

        private static async Task ActionsMediaAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember poster = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember moderator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            string mediaId = await ContentTestKit.MediaAsync(f, poster.Id, MediaKindEnum.Image, ct).ConfigureAwait(false);
            string postId = Api.Str(await ContentTestKit.PostAsync(f, poster.AccessToken, new { body = "with media", mediaIds = new[] { mediaId } }, ct).ConfigureAwait(false), "id");
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "FeedPost", postId, "Remove", ct).ConfigureAwait(false);
            Check.True((await f.Database.Media.ReadAsync(mediaId, ct).ConfigureAwait(false))!.IsRetained, "Remove moves served media to retained");
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "FeedPost", postId, "Restore", ct).ConfigureAwait(false);
            Check.True(!(await f.Database.Media.ReadAsync(mediaId, ct).ConfigureAwait(false))!.IsRetained, "Restore moves it back");
            Check.Equal(1L, await ModerationTestKit.AuditCountAsync(f, "moderation.remove", postId, ct).ConfigureAwait(false), "moderation.remove audited");
            Check.Equal(1L, await ModerationTestKit.AuditCountAsync(f, "moderation.restore", postId, ct).ConfigureAwait(false), "moderation.restore audited");

            string parent = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, postId, "parent", null, ct).ConfigureAwait(false);
            string child = await ContentTestKit.CommentIdAsync(f, poster.AccessToken, postId, "child", parent, ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "Comment", child, "Hide", ct).ConfigureAwait(false);
            await ContentTestKit.VerifyCountsAsync(f, postId, ct).ConfigureAwait(false);
            Check.Equal(1, (await f.Database.FeedPosts.ReadAsync(postId, ct).ConfigureAwait(false))!.CommentCount, "Hidden comment leaves commentCount");
            await ModerationTestKit.ActOkAsync(f, moderator.AccessToken, "Comment", child, "Unhide", ct).ConfigureAwait(false);
            await ContentTestKit.VerifyCountsAsync(f, postId, ct).ConfigureAwait(false);
            Check.Equal(2, (await f.Database.FeedPosts.ReadAsync(postId, ct).ConfigureAwait(false))!.CommentCount, "Unhidden comment counts again");
            Check.Equal(1L, await ModerationTestKit.AuditCountAsync(f, "moderation.hide", child, ct).ConfigureAwait(false), "moderation.hide audited");
            Check.Equal(1L, await ModerationTestKit.AuditCountAsync(f, "moderation.unhide", child, ct).ConfigureAwait(false), "moderation.unhide audited");
        }

        #endregion
    }
}
