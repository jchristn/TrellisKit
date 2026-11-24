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
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Ai;

    /// <summary>
    /// Phase 4 engagement plumbing (docs/PHASE4_CONTRACT.md sections 3.10, 4.8, 5.8, 5.11, 6, and 7) through a test-only
    /// content target provider: comments, reactions, reports, scoped moderation, staff content protection, auto-hide,
    /// global search visibility, promotion coupling, assistant knowledge types, and the assistant tool registry.
    /// </summary>
    public static class EngagementSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Engagement";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Phase 4 engagement (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Comments.TreeCreateEdit", "Comments on a provider object: create and reply through the helper routes, the GET alias and enumerate tree, edit and delete through /comments/{id}, provider hooks called", CommentsAsync),
                Case("Comments.Visibility", "A provider object the caller cannot read hides its tree, its comments, permalinks, and children (404); commentsEnabled false answers 403 CommentsDisabled", CommentVisibilityAsync),
                Case("Reactions.TargetAndComments", "Reactions on a provider object and on comments under it; who reacted lists them; ReactionsChanged is called", ReactionsAsync),
                Case("Reports.Scope", "Reports on provider objects and their comments are stored with the provider's scope type; reasons Inaccurate and PermanentlyClosed follow the provider; hooks run once", ReportsAsync),
                Case("Moderation.ScopedStaff", "LibraryEditor reaches the moderation routes and sees and acts only on Library items; EventManager and FeedModerator cannot act on Library content; members get 403", ScopedModerationAsync),
                Case("Moderation.SystemAdminProtection", "A scoped staff role never hides content authored by a SystemAdmin, and such reports stay out of its queue", ProtectionAsync),
                Case("Moderation.ForumModeratorComments", "Forum moderators moderate comments under a type when the provider says so, but not the objects themselves", ForumModeratorCommentsAsync),
                Case("Moderation.AutoHide", "Reports auto-hide provider objects only when the provider allows it", AutoHideAsync),
                Case("Search.ProviderTypes", "Global search indexes provider documents through sweeps, returns paths, honors filters.types, and drops objects the caller cannot read", SearchAsync),
                Case("Regression.Search.PunctuatedWords", "Hyphenated, bracketed, and @-prefixed words in content are found by their whole form and by each part", SearchPunctuationAsync),
                Case("Promotion.Coupling", "Promote a lesson: PromotedLesson card with source and commentTarget, 409 AlreadyPromoted, 409 UseCommentTarget on the card, hiding the source hides the card, unpromote deletes it", PromotionAsync),
                Case("Hardening.PropertyAuthored", "Objects without a member author (property content) take comments, reactions, reports, and Remove with notifyAuthor without errors; members cannot unpromote", PropertyAuthoredAsync),
                Case("Ai.KnowledgeAndTools", "Provider knowledge types become available with a provider; the tool registry resolves findGyms to findVenues, rejects duplicates, runs tools with bounds", AiAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return SuiteCases.Http(Id, caseId, name, body);
        }

        private static async Task CommentsAsync(HttpFixture f, CancellationToken ct)
        {
            TestTargetProvider provider = await EngagementTestKit.LibraryAsync(f, ct).ConfigureAwait(false);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Lesson Author").ConfigureAwait(false);
            TestMember member = await ContentTestKit.MemberAsync(f, ct, "Commenter").ConfigureAwait(false);
            TestTarget lesson = provider.Add(TargetTypeEnum.Lesson, "Getting started", author.Id);
            string prefix = EngagementTestKit.LessonPrefix + "/" + lesson.Id;

            string top = await EngagementTestKit.CommentAsync(f, member.AccessToken, EngagementTestKit.LessonPrefix, lesson.Id, "Great lesson", ct).ConfigureAwait(false);
            string reply = await EngagementTestKit.CommentAsync(f, author.AccessToken, EngagementTestKit.LessonPrefix, lesson.Id, "Thanks", ct, top).ConfigureAwait(false);
            Check.Equal(2, provider.Called("AfterComment"), "AfterComment per comment");

            JsonElement tree = await Api.ExpectAsync(f, HttpMethod.Post, prefix + "/comments/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> roots = tree.GetProperty("objects").EnumerateArray().ToList();
            Check.Equal(1, roots.Count, "One top-level comment");
            Check.Equal(top, Api.Str(roots[0], "id"), "Top comment");
            Check.Equal(reply, Api.Str(roots[0].GetProperty("children")[0], "id"), "Nested reply");
            Check.Equal(2L, tree.GetProperty("commentCount").GetInt64(), "commentCount from the provider");
            JsonElement alias = await Api.ExpectAsync(f, HttpMethod.Get, prefix + "/comments?depth=1", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, alias.GetProperty("objects").GetArrayLength(), "GET alias");

            JsonElement permalink = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/comments/" + reply, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Lesson", Api.Str(permalink.GetProperty("target"), "type"), "Permalink target type");
            JsonElement children = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/comments/" + top + "/children/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, children.GetProperty("objects").GetArrayLength(), "Children");

            int changed = provider.Called("CommentChanged");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + top, member.AccessToken, new { body = "Great lesson, edited" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(provider.Called("CommentChanged") > changed, "CommentChanged after edit");
            changed = provider.Called("CommentChanged");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/comments/" + reply, author.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.True(provider.Called("CommentChanged") > changed, "CommentChanged after delete");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, EngagementTestKit.LessonPrefix + "/tst_missing/comments", member.AccessToken, new { body = "Nope" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task CommentVisibilityAsync(HttpFixture f, CancellationToken ct)
        {
            TestTargetProvider provider = await EngagementTestKit.LibraryAsync(f, ct).ConfigureAwait(false);
            TestMember editor = await EngagementTestKit.RoleAsync(f, RoleEnum.LibraryEditor, ResourceTypeEnum.Library, ct, "Library Editor").ConfigureAwait(false);
            TestMember member = await ContentTestKit.MemberAsync(f, ct, "Reader").ConfigureAwait(false);
            TestTarget locked = provider.Add(TargetTypeEnum.Lesson, "Staff draft", editor.Id, t => t.StaffOnly = true);
            string comment = await EngagementTestKit.CommentAsync(f, editor.AccessToken, EngagementTestKit.LessonPrefix, locked.Id, "Draft note", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, EngagementTestKit.LessonPrefix + "/" + locked.Id + "/comments/enumerate", member.AccessToken, new { }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, EngagementTestKit.LessonPrefix + "/" + locked.Id + "/comments", member.AccessToken, new { body = "Hi" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/comments/" + comment, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/comments/" + comment + "/reactions/" + f.Property.Reactions[0].Key, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/comments/" + comment, editor.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);

            TestTarget closed = provider.Add(TargetTypeEnum.Lesson, "Closed lesson", editor.Id, t => t.CommentsEnabled = false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, EngagementTestKit.LessonPrefix + "/" + closed.Id + "/comments", member.AccessToken, new { body = "Hi" }, HttpStatusCode.Forbidden, "CommentsDisabled", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, EngagementTestKit.LessonPrefix + "/" + closed.Id + "/comments/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);

            // Hidden comments under the type: staff of the scope still see them; members do not.
            TestTarget open = provider.Add(TargetTypeEnum.Lesson, "Open lesson", editor.Id);
            string hidden = await EngagementTestKit.CommentAsync(f, member.AccessToken, EngagementTestKit.LessonPrefix, open.Id, "To be hidden", ct).ConfigureAwait(false);
            TestMember other = await ContentTestKit.MemberAsync(f, ct, "Other Reader").ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, editor.AccessToken, "Comment", hidden, "Hide", ct).ConfigureAwait(false);
            JsonElement staffTree = await Api.ExpectAsync(f, HttpMethod.Post, EngagementTestKit.LessonPrefix + "/" + open.Id + "/comments/enumerate", editor.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, staffTree.GetProperty("objects").GetArrayLength(), "Staff see the hidden comment");
            JsonElement memberTree = await Api.ExpectAsync(f, HttpMethod.Post, EngagementTestKit.LessonPrefix + "/" + open.Id + "/comments/enumerate", other.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, memberTree.GetProperty("objects").GetArrayLength(), "Members do not");
        }

        private static async Task ReactionsAsync(HttpFixture f, CancellationToken ct)
        {
            TestTargetProvider provider = await EngagementTestKit.LibraryAsync(f, ct).ConfigureAwait(false);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Lesson Author").ConfigureAwait(false);
            TestMember member = await ContentTestKit.MemberAsync(f, ct, "Reactor").ConfigureAwait(false);
            TestTarget lesson = provider.Add(TargetTypeEnum.Lesson, "Second lesson", author.Id);
            string key = f.Property.Reactions[0].Key;
            string prefix = EngagementTestKit.LessonPrefix + "/" + lesson.Id;
            JsonElement state = await Api.ExpectAsync(f, HttpMethod.Put, prefix + "/reactions/" + key, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, state.GetProperty("reactionTotal").GetInt64(), "One reaction");
            Check.Equal(1, provider.Called("ReactionsChanged"), "ReactionsChanged");
            JsonElement who = await Api.ExpectAsync(f, HttpMethod.Post, prefix + "/reactions/enumerate", author.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, who.GetProperty("objects").GetArrayLength(), "Who reacted");
            await Api.ExpectAsync(f, HttpMethod.Get, prefix + "/reactions", author.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement removed = await Api.ExpectAsync(f, HttpMethod.Delete, prefix + "/reactions/" + key, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0L, removed.GetProperty("reactionTotal").GetInt64(), "Removed");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, prefix + "/reactions/notakey", member.AccessToken, null, HttpStatusCode.BadRequest, "UnknownReaction", ct).ConfigureAwait(false);

            string comment = await EngagementTestKit.CommentAsync(f, author.AccessToken, EngagementTestKit.LessonPrefix, lesson.Id, "Comment to react to", ct).ConfigureAwait(false);
            JsonElement onComment = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/comments/" + comment + "/reactions/" + key, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1L, onComment.GetProperty("reactionTotal").GetInt64(), "Comment reaction");
            List<Notification> notes = await ForumTestKit.NotificationsAsync(f, author.Id, NotificationTypeEnum.Reaction, ct).ConfigureAwait(false);
            Check.True(notes.Any(n => n.Path != null && n.Path.StartsWith("/lessons/" + lesson.Id, StringComparison.Ordinal)), "Reaction notifications carry the provider path");
        }

        private static async Task ReportsAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Moderation.AutoHideReportThreshold = 0;
            TestTargetProvider provider = await EngagementTestKit.LibraryAsync(f, ct).ConfigureAwait(false);
            TestTargetProvider directory = EngagementTestKit.Install(f, "Directory", TargetTypeEnum.Venue, TargetTypeEnum.Instructor);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Author").ConfigureAwait(false);
            TestMember reporter = await ContentTestKit.MemberAsync(f, ct, "Reporter").ConfigureAwait(false);
            TestTarget lesson = provider.Add(TargetTypeEnum.Lesson, "Lesson three", author.Id);
            TestTarget venue = directory.Add(TargetTypeEnum.Venue, "Example Hall", author.Id);

            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "Lesson", lesson.Id, ct, "Inaccurate").ConfigureAwait(false);
            Check.Equal("Library", await EngagementTestKit.ScalarAsync(f, "SELECT scopetype FROM reports WHERE targetid = '" + lesson.Id + "';", ct).ConfigureAwait(false), "Lesson report scope");
            Check.Equal(1, provider.Called("RecomputeReports"), "RecomputeReports");
            Check.Equal(1, provider.Called("Reported:Inaccurate"), "ReportedAsync");
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "Lesson", lesson.Id, ct, "Inaccurate", HttpStatusCode.OK).ConfigureAwait(false);
            Check.Equal(1, provider.Called("Reported:Inaccurate"), "A duplicate report does not call ReportedAsync again");

            ApiResult closedLesson = await ModerationTestKit.ReportAsync(f, reporter.AccessToken, "Lesson", lesson.Id, ct, "PermanentlyClosed").ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, closedLesson.Status, "PermanentlyClosed does not apply to lessons");
            TestMember second = await ContentTestKit.MemberAsync(f, ct, "Second Reporter").ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, second.AccessToken, "Venue", venue.Id, ct, "PermanentlyClosed").ConfigureAwait(false);
            Check.Equal(1, directory.Called("Reported:PermanentlyClosed"), "Directory hook for a closure report");
            Check.Equal("Directory", await EngagementTestKit.ScalarAsync(f, "SELECT scopetype FROM reports WHERE targetid = '" + venue.Id + "';", ct).ConfigureAwait(false), "Venue report scope");

            string post = await ContentTestKit.PostIdAsync(f, (await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false)).AccessToken, "Feed post", ct).ConfigureAwait(false);
            ApiResult feedClosed = await ModerationTestKit.ReportAsync(f, reporter.AccessToken, "FeedPost", post, ct, "PermanentlyClosed").ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, feedClosed.Status, "New reasons do not apply to feed posts");

            string comment = await EngagementTestKit.CommentAsync(f, author.AccessToken, EngagementTestKit.LessonPrefix, lesson.Id, "Comment", ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "Comment", comment, ct).ConfigureAwait(false);
            Check.Equal("Library", await EngagementTestKit.ScalarAsync(f, "SELECT scopetype FROM reports WHERE targetid = '" + comment + "';", ct).ConfigureAwait(false), "Comment report scope");

            TestTarget hiddenFromMembers = provider.Add(TargetTypeEnum.Lesson, "Staff only", author.Id, t => t.StaffOnly = true);
            ApiResult invisible = await ModerationTestKit.ReportAsync(f, reporter.AccessToken, "Lesson", hiddenFromMembers.Id, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.NotFound, invisible.Status, "Cannot report what you cannot read");
            ApiResult self = await ModerationTestKit.ReportAsync(f, author.AccessToken, "Lesson", lesson.Id, ct).ConfigureAwait(false);
            Check.Equal("CannotReportSelf", self.Error, "Authors cannot report their own object");
        }

        private static async Task ScopedModerationAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Moderation.AutoHideReportThreshold = 0;
            TestTargetProvider library = await EngagementTestKit.LibraryAsync(f, ct).ConfigureAwait(false);
            TestTargetProvider events = EngagementTestKit.Install(f, "Events", TargetTypeEnum.Event);
            TestMember author = await ContentTestKit.PosterAsync(f, ct, "Author").ConfigureAwait(false);
            TestMember reporter = await ContentTestKit.MemberAsync(f, ct, "Reporter").ConfigureAwait(false);
            TestMember editor = await EngagementTestKit.RoleAsync(f, RoleEnum.LibraryEditor, ResourceTypeEnum.Library, ct, "Library Editor").ConfigureAwait(false);
            TestMember manager = await EngagementTestKit.RoleAsync(f, RoleEnum.EventManager, ResourceTypeEnum.Events, ct, "Event Manager").ConfigureAwait(false);
            TestMember feedModerator = await ModerationTestKit.ModeratorAsync(f, ct).ConfigureAwait(false);
            TestTarget lesson = library.Add(TargetTypeEnum.Lesson, "Intro session", author.Id);
            TestTarget evt = events.Add(TargetTypeEnum.Event, "Community meetup", author.Id);
            string post = await ContentTestKit.PostIdAsync(f, author.AccessToken, "A feed post", ct).ConfigureAwait(false);
            string comment = await EngagementTestKit.CommentAsync(f, author.AccessToken, EngagementTestKit.LessonPrefix, lesson.Id, "Lesson comment", ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "Lesson", lesson.Id, ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "Event", evt.Id, ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "FeedPost", post, ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "Comment", comment, ct).ConfigureAwait(false);

            await Api.ExpectErrorAsync(f, HttpMethod.Get, ModerationTestKit.Queue, reporter.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement queue = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue, editor.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> seen = ModerationTestKit.TargetIds(queue);
            Check.True(seen.Contains(lesson.Id) && seen.Contains(comment), "Library items in the editor's queue: " + String.Join(",", seen));
            Check.True(!seen.Contains(evt.Id) && !seen.Contains(post), "No Events or Feed items in the editor's queue");
            JsonElement lessonRow = queue.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "targetId") == lesson.Id);
            Check.Equal("Intro session", Api.Str(lessonRow.GetProperty("preview"), "title"), "Queue preview from the provider");
            Check.Equal("Library", Api.Str(lessonRow.GetProperty("scope"), "type"), "Queue scope");
            JsonElement filtered = await Api.ExpectAsync(f, HttpMethod.Post, ModerationTestKit.Queue + "/enumerate", HttpFixture.AdminToken, new { filters = new { scopeType = "Events" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ModerationTestKit.TargetIds(filtered).SequenceEqual(new[] { evt.Id }), "filters.scopeType Events");
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "/Lesson/" + lesson.Id, editor.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Intro session", Api.Str(detail.GetProperty("target"), "title"), "Detail renders the provider view");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, ModerationTestKit.Queue + "/Event/" + evt.Id, editor.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);

            await ModerationTestKit.ActOkAsync(f, editor.AccessToken, "Lesson", lesson.Id, "Hide", ct).ConfigureAwait(false);
            Check.Equal(ModerationStateEnum.Hidden, lesson.State, "Hidden through the provider");
            Check.Equal(0L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM reports WHERE targetid = '" + lesson.Id + "' AND status = 'Open';", ct).ConfigureAwait(false), "Reports resolved");
            await ModerationTestKit.ActOkAsync(f, editor.AccessToken, "Lesson", lesson.Id, "Unhide", ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, editor.AccessToken, "Lesson", lesson.Id, "Remove", ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, editor.AccessToken, "Lesson", lesson.Id, "Restore", ct).ConfigureAwait(false);
            Check.Equal(ModerationStateEnum.None, lesson.State, "Restored");
            await ModerationTestKit.ActOkAsync(f, editor.AccessToken, "Comment", comment, "Hide", ct).ConfigureAwait(false);

            ApiResult managerOnLesson = await ModerationTestKit.ActAsync(f, manager.AccessToken, "Lesson", lesson.Id, "Hide", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, managerOnLesson.Status, "EventManager cannot act on a lesson");
            ApiResult managerOnComment = await ModerationTestKit.ActAsync(f, manager.AccessToken, "Comment", comment, "Unhide", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, managerOnComment.Status, "EventManager cannot act on a lesson comment");
            ApiResult feedOnComment = await ModerationTestKit.ActAsync(f, feedModerator.AccessToken, "Comment", comment, "Unhide", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, feedOnComment.Status, "FeedModerator cannot act on a lesson comment");
            ApiResult editorOnPost = await ModerationTestKit.ActAsync(f, editor.AccessToken, "FeedPost", post, "Hide", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, editorOnPost.Status, "LibraryEditor cannot act on feed posts");
            await ModerationTestKit.ActOkAsync(f, manager.AccessToken, "Event", evt.Id, "Hide", ct).ConfigureAwait(false);
            JsonElement managerQueue = await Api.ExpectAsync(f, HttpMethod.Post, ModerationTestKit.Queue + "/enumerate", manager.AccessToken, new { filters = new { status = "Resolved" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(ModerationTestKit.TargetIds(managerQueue).All(i => i == evt.Id), "EventManager's resolved queue holds only Events items");
            JsonElement summary = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/moderation/summary", editor.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(summary.ValueKind == JsonValueKind.Object, "Summary in scope");
        }

        private static async Task ProtectionAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Moderation.AutoHideReportThreshold = 0;
            TestTargetProvider library = await EngagementTestKit.LibraryAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember editor = await EngagementTestKit.RoleAsync(f, RoleEnum.LibraryEditor, ResourceTypeEnum.Library, ct, "Library Editor").ConfigureAwait(false);
            TestMember reporter = await ContentTestKit.MemberAsync(f, ct, "Reporter").ConfigureAwait(false);
            TestTarget adminLesson = library.Add(TargetTypeEnum.Lesson, "Admin lesson", admin.Id);
            string adminComment = await EngagementTestKit.CommentAsync(f, admin.AccessToken, EngagementTestKit.LessonPrefix, adminLesson.Id, "Admin comment", ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "Lesson", adminLesson.Id, ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "Comment", adminComment, ct).ConfigureAwait(false);
            ApiResult hideLesson = await ModerationTestKit.ActAsync(f, editor.AccessToken, "Lesson", adminLesson.Id, "Hide", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, hideLesson.Status, "No action on a SystemAdmin's lesson");
            ApiResult hideComment = await ModerationTestKit.ActAsync(f, editor.AccessToken, "Comment", adminComment, "Hide", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, hideComment.Status, "No action on a SystemAdmin's comment");
            JsonElement queue = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue, editor.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, ModerationTestKit.TargetIds(queue).Count, "SystemAdmin content stays out of the scoped queue");
            await ModerationTestKit.ActOkAsync(f, HttpFixture.AdminToken, "Lesson", adminLesson.Id, "Hide", ct).ConfigureAwait(false);
            Check.Equal(ModerationStateEnum.Hidden, adminLesson.State, "SystemAdmin may act");
        }

        private static async Task ForumModeratorCommentsAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Moderation.AutoHideReportThreshold = 0;
            TestTargetProvider library = await EngagementTestKit.LibraryAsync(f, ct).ConfigureAwait(false);
            library.ForumModeratorsModerateComments = true;
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Author").ConfigureAwait(false);
            TestMember reporter = await ContentTestKit.MemberAsync(f, ct, "Reporter").ConfigureAwait(false);
            TestMember forumModerator = await EngagementTestKit.RoleAsync(f, RoleEnum.Moderator, ResourceTypeEnum.Global, ct, "Forum Moderator").ConfigureAwait(false);
            TestTarget lesson = library.Add(TargetTypeEnum.Lesson, "Lesson five", author.Id);
            string comment = await EngagementTestKit.CommentAsync(f, author.AccessToken, EngagementTestKit.LessonPrefix, lesson.Id, "Rude comment", ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "Lesson", lesson.Id, ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "Comment", comment, ct).ConfigureAwait(false);
            JsonElement queue = await Api.ExpectAsync(f, HttpMethod.Get, ModerationTestKit.Queue, forumModerator.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> seen = ModerationTestKit.TargetIds(queue);
            Check.True(seen.Contains(comment) && !seen.Contains(lesson.Id), "Comment reports only: " + String.Join(",", seen));
            await ModerationTestKit.ActOkAsync(f, forumModerator.AccessToken, "Comment", comment, "Hide", ct).ConfigureAwait(false);
            ApiResult onLesson = await ModerationTestKit.ActAsync(f, forumModerator.AccessToken, "Lesson", lesson.Id, "Hide", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, onLesson.Status, "Not the lesson itself");
            library.ForumModeratorsModerateComments = false;
            f.Services.Tokens.InvalidateUser(forumModerator.Id);
            ApiResult off = await ModerationTestKit.ActAsync(f, forumModerator.AccessToken, "Comment", comment, "Unhide", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, off.Status, "Setting off: no comment moderation");
        }

        private static async Task AutoHideAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Moderation.AutoHideReportThreshold = 1;
            TestTargetProvider library = await EngagementTestKit.LibraryAsync(f, ct).ConfigureAwait(false);
            TestTargetProvider directory = EngagementTestKit.Install(f, "Directory", TargetTypeEnum.Venue);
            library.AutoHide = true;
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Author").ConfigureAwait(false);
            TestMember reporter = await ContentTestKit.MemberAsync(f, ct, "Reporter").ConfigureAwait(false);
            TestTarget lesson = library.Add(TargetTypeEnum.Lesson, "Spammy lesson", author.Id);
            TestTarget venue = directory.Add(TargetTypeEnum.Venue, "Competitor venue", author.Id);
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "Lesson", lesson.Id, ct).ConfigureAwait(false);
            Check.Equal(ModerationStateEnum.Hidden, lesson.State, "Provider state hidden");
            Check.Equal(1L, await ModerationTestKit.AutoHideCountAsync(f, lesson.Id, ct).ConfigureAwait(false), "AutoHide action logged");
            await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "Venue", venue.Id, ct).ConfigureAwait(false);
            Check.Equal(ModerationStateEnum.None, venue.State, "Directory entries only queue");
            Check.Equal(0L, await ModerationTestKit.AutoHideCountAsync(f, venue.Id, ct).ConfigureAwait(false), "No AutoHide action on the venue");
        }

        private static async Task SearchAsync(HttpFixture f, CancellationToken ct)
        {
            TestTargetProvider library = await EngagementTestKit.LibraryAsync(f, ct).ConfigureAwait(false);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Author").ConfigureAwait(false);
            TestMember member = await ContentTestKit.MemberAsync(f, ct, "Searcher").ConfigureAwait(false);
            TestMember editor = await EngagementTestKit.RoleAsync(f, RoleEnum.LibraryEditor, ResourceTypeEnum.Library, ct, "Library Editor").ConfigureAwait(false);
            TestTarget open = library.Add(TargetTypeEnum.Lesson, "Zephyrine workshop", author.Id);
            TestTarget locked = library.Add(TargetTypeEnum.Lesson, "Zephyrine secret drill", author.Id, t => t.StaffOnly = true);
            TestTarget series = library.Add(TargetTypeEnum.Series, "Zephyrine series", author.Id);
            await ForumTestKit.DrainSearchAsync(f, ct).ConfigureAwait(false);

            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", member.AccessToken, new { search = "zephyrine" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> hits = page.GetProperty("objects").EnumerateArray().ToList();
            List<string> ids = hits.Select(h => Api.Str(h, "id")).ToList();
            Check.True(ids.Contains(open.Id) && ids.Contains(series.Id), "Visible lesson and series found: " + String.Join(",", ids));
            Check.True(!ids.Contains(locked.Id), "Locked lesson is not a result");
            JsonElement hit = hits.First(h => Api.Str(h, "id") == open.Id);
            Check.Equal("Lesson", Api.Str(hit, "type"), "Type");
            Check.Equal("/lessons/" + open.Id, Api.Str(hit, "path"), "Path from the provider");
            Check.Equal("Zephyrine workshop", Api.Str(hit, "title"), "Title from the provider");

            JsonElement typed = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", member.AccessToken, new { search = "zephyrine", filters = new { types = new[] { "Series" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(typed.GetProperty("objects").EnumerateArray().All(h => Api.Str(h, "type") == "Series") && typed.GetProperty("objects").GetArrayLength() == 1, "filters.types Series");
            JsonElement staff = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", editor.AccessToken, new { search = "zephyrine" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(staff.GetProperty("objects").EnumerateArray().Any(h => Api.Str(h, "id") == locked.Id), "Staff who can read it find it");

            open.State = ModerationStateEnum.Hidden;
            await f.Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Lesson, open.Id, ct).ConfigureAwait(false);
            await f.Services.Forums.Search.DrainAsync(100, ct).ConfigureAwait(false);
            JsonElement after = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", member.AccessToken, new { search = "workshop" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(after.GetProperty("objects").EnumerateArray().All(h => Api.Str(h, "id") != open.Id), "Removed from the index when the provider returns no document");
        }

        private static async Task SearchPunctuationAsync(HttpFixture f, CancellationToken ct)
        {
            TestTargetProvider library = await EngagementTestKit.LibraryAsync(f, ct).ConfigureAwait(false);
            TestMember author = await ContentTestKit.MemberAsync(f, ct, "Punctuation Author").ConfigureAwait(false);
            TestMember member = await ContentTestKit.MemberAsync(f, ct, "Punctuation Searcher").ConfigureAwait(false);
            TestTarget lesson = library.Add(TargetTypeEnum.Lesson, "Quorvexa-drills [warmup] with @tavrinel", author.Id);
            await ForumTestKit.DrainSearchAsync(f, ct).ConfigureAwait(false);
            foreach (string query in new[] { "quorvexa-drills", "quorvexa", "drills quorvexa", "warmup", "tavrinel", "@tavrinel" })
            {
                JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/search/enumerate", member.AccessToken, new { search = query }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.True(page.GetProperty("objects").EnumerateArray().Any(h => Api.Str(h, "id") == lesson.Id), "Found by \"" + query + "\"");
            }
        }

        private static async Task PromotionAsync(HttpFixture f, CancellationToken ct)
        {
            TestTargetProvider library = await EngagementTestKit.LibraryAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await ContentTestKit.MemberAsync(f, ct, "Member").ConfigureAwait(false);
            TestMember editor = await EngagementTestKit.RoleAsync(f, RoleEnum.LibraryEditor, ResourceTypeEnum.Library, ct, "Library Editor").ConfigureAwait(false);
            TestMember reporter = await ContentTestKit.MemberAsync(f, ct, "Reporter").ConfigureAwait(false);
            TestTarget lesson = library.Add(TargetTypeEnum.Lesson, "Promoted lesson", editor.Id);
            string prefix = EngagementTestKit.LessonPrefix + "/" + lesson.Id;

            await Api.ExpectErrorAsync(f, HttpMethod.Post, prefix + "/promote", member.AccessToken, new { }, HttpStatusCode.Forbidden, "PromotionNotAllowed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, prefix + "/promote", admin.AccessToken, new { audience = "Public" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement card = await Api.ExpectAsync(f, HttpMethod.Post, prefix + "/promote", admin.AccessToken, new { notifyMembers = false }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            string cardId = Api.Str(card, "id");
            Check.Equal("PromotedLesson", Api.Str(card, "kind"), "Card kind");
            Check.Equal("Lesson", Api.Str(card.GetProperty("commentTarget"), "type"), "commentTarget type");
            Check.Equal(lesson.Id, Api.Str(card.GetProperty("commentTarget"), "id"), "commentTarget id");
            Check.Equal("Promoted lesson", Api.Str(card.GetProperty("source"), "title"), "source title");
            Check.Equal(cardId, lesson.PromotedFeedPostId, "PromotionChanged stored the card");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, prefix + "/promote", admin.AccessToken, new { }, HttpStatusCode.Conflict, "AlreadyPromoted", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/feed/" + cardId + "/comments", member.AccessToken, new { body = "On the card" }, HttpStatusCode.Conflict, "UseCommentTarget", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/feed/" + cardId + "/reactions/" + f.Property.Reactions[0].Key, member.AccessToken, null, HttpStatusCode.Conflict, "UseCommentTarget", ct).ConfigureAwait(false);
            await EngagementTestKit.CommentAsync(f, member.AccessToken, EngagementTestKit.LessonPrefix, lesson.Id, "Comment on the source", ct).ConfigureAwait(false);
            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed/" + cardId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, read.GetProperty("commentCount").GetInt32(), "Card shows the source's comment count");
            Check.True(await InFeedAsync(f, member, cardId, ct).ConfigureAwait(false), "Card in the feed");

            JsonElement report = await ModerationTestKit.ReportOkAsync(f, reporter.AccessToken, "FeedPost", cardId, ct).ConfigureAwait(false);
            Check.True(report.ValueKind == JsonValueKind.Object, "Card report accepted");
            Check.Equal("Lesson", await EngagementTestKit.ScalarAsync(f, "SELECT targettype FROM reports WHERE targetid = '" + lesson.Id + "';", ct).ConfigureAwait(false), "Card reports resolve to the source");

            await ModerationTestKit.ActOkAsync(f, editor.AccessToken, "Lesson", lesson.Id, "Hide", ct).ConfigureAwait(false);
            Check.True(!await InFeedAsync(f, member, cardId, ct).ConfigureAwait(false), "Hiding the source hides the card");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/feed/" + cardId, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, editor.AccessToken, "Lesson", lesson.Id, "Unhide", ct).ConfigureAwait(false);
            Check.True(await InFeedAsync(f, member, cardId, ct).ConfigureAwait(false), "Unhiding brings it back");

            JsonElement unpromoted = await Api.ExpectAsync(f, HttpMethod.Post, prefix + "/unpromote", editor.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(unpromoted.GetProperty("unpromoted").GetBoolean(), "Unpromoted");
            Check.Equal(null, lesson.PromotedFeedPostId, "PromotionChanged cleared");
            Check.True(!await InFeedAsync(f, member, cardId, ct).ConfigureAwait(false), "Card deleted");
            JsonElement again = await Api.ExpectAsync(f, HttpMethod.Post, prefix + "/unpromote", editor.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!again.GetProperty("unpromoted").GetBoolean(), "Idempotent");

            await Api.ExpectAsync(f, HttpMethod.Post, prefix + "/promote", admin.AccessToken, new { }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            lesson.Deleted = true;
            await f.Services.Forums.Promotions.SyncTargetAsync(TargetTypeEnum.Lesson, lesson.Id, ct).ConfigureAwait(false);
            Check.Equal(0L, await ModerationTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM feedposts WHERE sourceid = '" + lesson.Id + "' AND deletedutc IS NULL;", ct).ConfigureAwait(false), "A deleted source ends the promotion");
        }

        private static async Task PropertyAuthoredAsync(HttpFixture f, CancellationToken ct)
        {
            f.Settings.Moderation.AutoHideReportThreshold = 0;
            TestTargetProvider library = await EngagementTestKit.LibraryAsync(f, ct).ConfigureAwait(false);
            TestMember member = await ContentTestKit.MemberAsync(f, ct, "Member").ConfigureAwait(false);
            TestMember editor = await EngagementTestKit.RoleAsync(f, RoleEnum.LibraryEditor, ResourceTypeEnum.Library, ct, "Library Editor").ConfigureAwait(false);
            TestTarget lesson = library.Add(TargetTypeEnum.Lesson, "Property lesson", String.Empty);
            string prefix = EngagementTestKit.LessonPrefix + "/" + lesson.Id;
            await EngagementTestKit.CommentAsync(f, member.AccessToken, EngagementTestKit.LessonPrefix, lesson.Id, "Hello", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, prefix + "/reactions/" + f.Property.Reactions[0].Key, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ModerationTestKit.ReportOkAsync(f, member.AccessToken, "Lesson", lesson.Id, ct, "Inaccurate").ConfigureAwait(false);
            await ModerationTestKit.ActOkAsync(f, editor.AccessToken, "Lesson", lesson.Id, "Remove", ct, "Outdated", true).ConfigureAwait(false);
            Check.Equal(ModerationStateEnum.Removed, lesson.State, "Removed");
            await ModerationTestKit.ActOkAsync(f, editor.AccessToken, "Lesson", lesson.Id, "Restore", ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            JsonElement card = await Api.ExpectAsync(f, HttpMethod.Post, prefix + "/promote", admin.AccessToken, new { }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.Equal(admin.Id, Api.Str(card.GetProperty("author"), "id"), "A property-authored card is attributed to the promoter");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, prefix + "/unpromote", member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
        }

        private static async Task<bool> InFeedAsync(HttpFixture f, TestMember member, string cardId, CancellationToken ct)
        {
            JsonElement feed = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/feed?maxResults=50", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            return feed.GetProperty("objects").EnumerateArray().Any(p => Api.Str(p, "id") == cardId);
        }

        private static async Task AiAsync(HttpFixture f, CancellationToken ct)
        {
            Check.True(!KnowledgeTypes.IsAvailable(f.Services, TargetTypeEnum.Lesson) || f.Services.ContentTargets.Find(TargetTypeEnum.Lesson) != null, "Lesson knowledge needs a provider");
            TestTargetProvider library = await EngagementTestKit.LibraryAsync(f, ct).ConfigureAwait(false);
            TestTargetProvider directory = EngagementTestKit.Install(f, "Directory", TargetTypeEnum.Venue);
            Check.True(KnowledgeTypes.IsAvailable(f.Services, TargetTypeEnum.Lesson) && KnowledgeTypes.IsAvailable(f.Services, TargetTypeEnum.Series) && KnowledgeTypes.IsAvailable(f.Services, TargetTypeEnum.Venue), "Provider knowledge types available");
            Check.True(!KnowledgeTypes.IsAvailable(f.Services, TargetTypeEnum.Instructor), "Instructor is not a knowledge type");
            Check.True(KnowledgeTypes.Available(f.Services).Contains("Lesson"), "Listed");
            library.Add(TargetTypeEnum.Lesson, "Knowledge lesson", "usr_x");
            List<KnowledgeDocument> docs = await library.KnowledgeDocumentsAsync(TargetTypeEnum.Lesson, new AssistantKnowledgeSource { TargetType = TargetTypeEnum.Lesson }, 10, ct).ConfigureAwait(false);
            Check.Equal(1, docs.Count, "Provider documents");

            AssistantToolRegistry tools = f.Services.Ai.Tools;
            tools.Unregister("findVenues");
            tools.Register(new AssistantTool
            {
                Name = "findVenues",
                Description = "Find venues",
                WorksSignedOut = true,
                Handler = (caller, args, token) => Task.FromResult(new AssistantToolResult { Text = new string('x', 10000), ResultCount = 3 })
            });
            Check.Equal("findVenues", tools.Canonical("findGyms"), "findGyms alias");
            Check.Equal("findVenues", tools.Canonical("FINDVENUES"), "Case-insensitive");
            List<string> allowed = tools.NormalizeAllowed(new[] { "findGyms", "findVenues", "noSuchTool" }, out List<string> unknown);
            Check.True(allowed.SequenceEqual(new[] { "findVenues" }), "Normalized allowed list");
            Check.True(unknown.SequenceEqual(new[] { "noSuchTool" }), "Unknown names reported");
            Check.Throws<InvalidOperationException>(() => tools.Register(new AssistantTool { Name = "findGyms", Handler = (c, a, t) => Task.FromResult(new AssistantToolResult()) }), "An alias cannot be registered as a tool");
            Check.Throws<InvalidOperationException>(() => tools.Register(new AssistantTool { Name = "findVenues", Handler = (c, a, t) => Task.FromResult(new AssistantToolResult()) }), "Duplicate name");
            using (JsonDocument args = JsonDocument.Parse("{}"))
            {
                AssistantToolResult result = await tools.InvokeAsync(new AssistantCaller(), "findGyms", args.RootElement, ct).ConfigureAwait(false);
                Check.Equal("Succeeded", result.Status, "Ran through the alias");
                Check.Equal(tools.MaxOutputCharacters, result.Text.Length, "Output bounded");
                AssistantToolResult missing = await tools.InvokeAsync(new AssistantCaller(), "noSuchTool", args.RootElement, ct).ConfigureAwait(false);
                Check.Equal("Rejected", missing.Status, "Unknown tool rejected");
            }
            tools.Register(new AssistantTool { Name = "testMembersOnly", Handler = (c, a, t) => Task.FromResult(new AssistantToolResult { Text = "ok" }) });
            Check.Equal(1, tools.ForCaller(new AssistantCaller(), new[] { "findGyms", "testMembersOnly" }).Count, "Visitors only get signed-out tools");
            Check.Equal(2, tools.ForCaller(new AssistantCaller { UserId = "usr_member" }, new[] { "findGyms", "testMembersOnly" }).Count, "Members get both");
            tools.Unregister("testMembersOnly");
            Check.True(directory.TargetTypes.Contains(TargetTypeEnum.Venue), "Directory provider installed");
        }

        #endregion
    }
}
