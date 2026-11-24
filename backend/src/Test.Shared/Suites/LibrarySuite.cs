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

    /// <summary>
    /// Phase 4 library (docs/PHASE4_CONTRACT.md sections 5.1 to 5.4, 5.7, 5.10, 8): series and lesson lifecycle,
    /// scheduling with Processing media, ordering and moves, archive with the playback window, slugs and redirects,
    /// levels from every source, LibraryEditor permissions, the property document's library section, and
    /// NewLessonInSeries aggregation.
    /// </summary>
    public static class LibrarySuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Library";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Library (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Library.Lifecycle", "Staff create Draft series and lessons members cannot see; publishing lists them with recomputed counters; a series with lessons cannot be deleted (409 SeriesNotEmpty); deleting lessons then the series works; actions are audited", LifecycleAsync),
                Case("Library.Scheduling", "Video lessons need a video (400 LessonVideoRequired); publishing with Processing media gives Processing until the media is Ready; due Scheduled lessons publish through the worker once Ready; future schedules wait", SchedulingAsync),
                Case("Library.Ordering", "Series order 10, 20, 30; lesson order is a permutation of the series; moves close gaps and shift the target; neighbors follow positions", OrderingAsync),
                Case("Library.Archive", "Archived lessons leave lists; members with progress keep reading them and see them in Continue watching; others get 404; unarchive restores", ArchiveAsync),
                Case("Library.Slugs", "Slugs are unique per property (series) and per series (lessons) with 409 SlugInUse; old slugs keep redirecting after a change", SlugsAsync),
                Case("Library.Levels", "Levels come from Custom definitions or a profile field; unknown keys give 400 UnknownOption; levels enumerate and appear in GET /api/v1/property; only SystemAdmin edits settings and levels", LevelsAsync),
                Case("Library.Editor", "LibraryEditor manages series and lessons and uploads documents but cannot change library settings; members get 403 on staff routes and cannot upload documents", EditorAsync),
                Case("Library.FollowNotifications", "Followers get NewLessonInSeries on publish, aggregated per series while unread; unfollowed members get nothing; staff get PostComment for notifyStaffOnComment lessons", NotificationsAsync)
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

        private static async Task LifecycleAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement draft = await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Getting started", ct, publish: false).ConfigureAwait(false);
            string seriesId = Api.Str(draft, "id");
            Check.Equal("Draft", Api.Str(draft, "status"), "New series are Draft");
            Check.Equal("getting-started", Api.Str(draft, "slug"), "Derived slug");
            Check.Equal("Members", Api.Str(draft, "audience"), "Gated library defaults to Members");
            ContentTestKit.CheckMetadata(draft, "series");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/series/" + seriesId, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement lesson = await LibraryTestKit.LessonAsync(f, admin.AccessToken, seriesId, "First steps", ct, publish: false).ConfigureAwait(false);
            string lessonId = Api.Str(lesson, "id");
            Check.Equal("Draft", Api.Str(lesson, "status"), "New lessons are Draft");
            Check.Equal(1, lesson.GetProperty("position").GetInt32(), "First position");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/series/" + seriesId + "/publish", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement series = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/series/" + seriesId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, series.GetProperty("lessonCount").GetInt32(), "Draft lessons are not counted");
            JsonElement published = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/" + lessonId + "/publish", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Published", Api.Str(published, "status"), "Article publishes at once");
            series = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/series/getting-started", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, series.GetProperty("lessonCount").GetInt32(), "lessonCount recomputed");
            Check.True(series.GetProperty("viewer").GetProperty("canOpen").GetBoolean() && !series.GetProperty("locked").GetBoolean(), "Members open the series");
            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/series/" + seriesId + "/lessons/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(LibraryTestKit.Ids(list).SequenceEqual(new[] { lessonId }), "Series lessons");
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + lessonId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("First steps article body", Api.Str(detail, "description"), "Article body");
            Check.Equal("Lesson", Api.Str(detail.GetProperty("commentTarget"), "type"), "commentTarget is the lesson");
            Check.True(detail.GetProperty("viewer").GetProperty("canComment").GetBoolean(), "Members comment");
            JsonElement lessons = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/enumerate", member.AccessToken, new { filters = new { kind = "Article" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(LibraryTestKit.Ids(lessons).Contains(lessonId), "Cross-series list with a filter");
            JsonElement comment = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/" + lessonId + "/comments", member.AccessToken, new { body = "Great lesson" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.True(Api.Str(comment, "id").Length > 0, "Lesson comment created");
            detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + lessonId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, detail.GetProperty("commentCount").GetInt32(), "commentCount recomputed");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/lessons/" + lessonId + "/reactions/" + f.Property.Reactions[0].Key, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + lessonId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, detail.GetProperty("reactionTotal").GetInt32(), "reactionTotal recomputed");
            Check.Equal(1, detail.GetProperty("myReactions").GetArrayLength(), "myReactions");
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, "/api/v1/admin/series/" + seriesId, admin.AccessToken, null, HttpStatusCode.Conflict, "SeriesNotEmpty", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/lessons/" + lessonId, admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/lessons/" + lessonId, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            series = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/series/" + seriesId, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, series.GetProperty("lessonCount").GetInt32(), "Counter after delete");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/series/" + seriesId, admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.True(await ModerationTestKit.AuditCountAsync(f, "series.create", seriesId, ct).ConfigureAwait(false) >= 1, "series.create audited");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "lesson.publish", lessonId, ct).ConfigureAwait(false) >= 1, "lesson.publish audited");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "lesson.delete", lessonId, ct).ConfigureAwait(false) >= 1, "lesson.delete audited");
        }

        private static async Task SchedulingAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string seriesId = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Video course", ct).ConfigureAwait(false), "id");
            JsonElement empty = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/series/" + seriesId + "/lessons", admin.AccessToken, new { title = "No video yet", kind = "Video" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/" + Api.Str(empty, "id") + "/publish", admin.AccessToken, new { }, HttpStatusCode.BadRequest, "LessonVideoRequired", ct).ConfigureAwait(false);
            string processing = await LibraryTestKit.VideoAsync(f, admin.Id, 300, ct, MediaStatusEnum.Processing).ConfigureAwait(false);
            JsonElement lesson = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/lessons/" + Api.Str(empty, "id"), admin.AccessToken, new { videoMediaId = processing }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string lessonId = Api.Str(lesson, "id");
            JsonElement published = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/" + lessonId + "/publish", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Processing", Api.Str(published, "status"), "Processing until the video is Ready");
            Check.Equal("Processing", Api.Str(published, "mediaStatus"), "Staff see the media status");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/lessons/" + lessonId, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await f.Services.Library.Lessons.PublishDueAsync(100, ct).ConfigureAwait(false);
            Check.Equal("Processing", Api.Str(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/lessons/" + lessonId, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false), "status"), "Not Ready: still Processing");
            await LibraryTestKit.MakeReadyAsync(f, processing, 300.4, ct).ConfigureAwait(false);
            Check.Equal(1, await f.Services.Library.Lessons.PublishDueAsync(100, ct).ConfigureAwait(false), "The worker publishes it once Ready");
            JsonElement live = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + lessonId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Published", Api.Str(live, "status"), "Published");
            Check.Equal(300, live.GetProperty("durationSeconds").GetInt32(), "Duration from the media");
            JsonElement series = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/series/" + seriesId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(300, series.GetProperty("totalDurationSeconds").GetInt32(), "totalDurationSeconds recomputed");

            JsonElement later = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/series/" + seriesId + "/lessons", admin.AccessToken, new { title = "Later lesson", kind = "Article", description = "Text" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            JsonElement scheduled = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/" + Api.Str(later, "id") + "/schedule", admin.AccessToken, new { publishAtUtc = DateTime.UtcNow.AddHours(2) }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Scheduled", Api.Str(scheduled, "status"), "Scheduled");
            await f.Services.Library.Lessons.PublishDueAsync(100, ct).ConfigureAwait(false);
            Check.Equal("Scheduled", Api.Str(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/lessons/" + Api.Str(later, "id"), admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false), "status"), "Future schedules wait");
            await LibraryTestKit.SqlAsync(f, "UPDATE lessons SET publishatutc = now() - interval '1 minute' WHERE id = '" + Api.Str(later, "id") + "';", ct).ConfigureAwait(false);
            Check.Equal(1, await f.Services.Library.Lessons.PublishDueAsync(100, ct).ConfigureAwait(false), "Due Scheduled article publishes");
            string scheduledVideo = await LibraryTestKit.VideoAsync(f, admin.Id, 120, ct, MediaStatusEnum.Processing).ConfigureAwait(false);
            JsonElement third = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/series/" + seriesId + "/lessons", admin.AccessToken, new { title = "Scheduled video", kind = "Video", videoMediaId = scheduledVideo }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/" + Api.Str(third, "id") + "/schedule", admin.AccessToken, new { publishAtUtc = DateTime.UtcNow.AddSeconds(-5) }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Processing", Api.Str(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/lessons/" + Api.Str(third, "id"), admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false), "status"), "Due with Processing media becomes Processing");
            await LibraryTestKit.MakeReadyAsync(f, scheduledVideo, 120, ct).ConfigureAwait(false);
            MediaAttachment ready = (await f.Database.Media.ReadAsync(scheduledVideo, ct).ConfigureAwait(false))!;
            await f.Services.Library.Lessons.OnMediaSettledAsync(ready, ct).ConfigureAwait(false);
            Check.Equal("Published", Api.Str(await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/lessons/" + Api.Str(third, "id"), admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false), "status"), "Media settled publishes it");
        }

        private static async Task OrderingAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string a = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Series A", ct).ConfigureAwait(false), "id");
            string b = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Series B", ct).ConfigureAwait(false), "id");
            string c = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Series C", ct).ConfigureAwait(false), "id");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/series/order", admin.AccessToken, new { seriesIds = new[] { c, a, b } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/series/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(LibraryTestKit.Ids(list).SequenceEqual(new[] { c, a, b }), "Member order follows the admin order");
            Check.Equal(10, LibraryTestKit.Find(list, c).GetProperty("sortOrder").GetInt32(), "Sort orders 10, 20, 30");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/series/order", admin.AccessToken, new { seriesIds = new[] { c, a } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            List<string> lessons = new List<string>();
            for (int i = 1; i <= 4; i++) lessons.Add(Api.Str(await LibraryTestKit.LessonAsync(f, admin.AccessToken, a, "Lesson " + i, ct).ConfigureAwait(false), "id"));
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/series/" + a + "/lessons/order", admin.AccessToken, new { lessonIds = new[] { lessons[3], lessons[0], lessons[1], lessons[2] } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement ordered = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/series/" + a + "/lessons/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(LibraryTestKit.Ids(ordered).SequenceEqual(new[] { lessons[3], lessons[0], lessons[1], lessons[2] }), "Lesson order");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/series/" + a + "/lessons/order", admin.AccessToken, new { lessonIds = new[] { lessons[0] } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + lessons[0], member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(lessons[3], Api.Str(detail.GetProperty("previous"), "id"), "Previous neighbor");
            Check.Equal(lessons[1], Api.Str(detail.GetProperty("next"), "id"), "Next neighbor");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/" + lessons[0] + "/move", admin.AccessToken, new { seriesId = b, position = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement left = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/series/" + a + "/lessons/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(left.GetProperty("objects").EnumerateArray().Select(o => o.GetProperty("position").GetInt32()).SequenceEqual(new[] { 1, 2, 3 }), "The old series closes the gap");
            string bLesson = Api.Str(await LibraryTestKit.LessonAsync(f, admin.AccessToken, b, "B existing", ct).ConfigureAwait(false), "id");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/" + lessons[1] + "/move", admin.AccessToken, new { seriesId = b, position = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement target = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/series/" + b + "/lessons/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(LibraryTestKit.Ids(target).SequenceEqual(new[] { lessons[1], lessons[0], bLesson }), "Moves shift the target");
            Check.True(target.GetProperty("objects").EnumerateArray().Select(o => o.GetProperty("position").GetInt32()).SequenceEqual(new[] { 1, 2, 3 }), "Target positions");
            JsonElement sa = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/series/" + a, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement sb = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/series/" + b, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, sa.GetProperty("lessonCount").GetInt32(), "Source counter");
            Check.Equal(3, sb.GetProperty("lessonCount").GetInt32(), "Target counter");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "lesson.move", lessons[0], ct).ConfigureAwait(false) >= 1, "lesson.move audited");
        }

        private static async Task ArchiveAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember watcher = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string seriesId = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Archive course", ct).ConfigureAwait(false), "id");
            string lessonId = await LibraryTestKit.VideoLessonAsync(f, admin, seriesId, "Soon archived", 600, ct).ConfigureAwait(false);
            await LibraryTestKit.ProgressAsync(f, watcher.AccessToken, lessonId, 100, 20, DateTime.UtcNow, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/" + lessonId + "/archive", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/enumerate", watcher.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!LibraryTestKit.Ids(list).Contains(lessonId), "Archived lessons leave lists");
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + lessonId, watcher.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Archived", Api.Str(detail, "status"), "The watcher keeps the lesson");
            JsonElement cont = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/library/continue/enumerate", watcher.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(cont.GetProperty("objects").EnumerateArray().Any(o => Api.Str(o.GetProperty("lesson"), "id") == lessonId), "Continue watching keeps it");
            await LibraryTestKit.ProgressAsync(f, watcher.AccessToken, lessonId, 150, 20, DateTime.UtcNow, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/lessons/" + lessonId, other.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            f.Settings.Library.ArchivedPlaybackDays = 0;
            await LibraryTestKit.SqlAsync(f, "UPDATE lessons SET archivedutc = now() - interval '2 days' WHERE id = '" + lessonId + "';", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/lessons/" + lessonId, watcher.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            f.Settings.Library.ArchivedPlaybackDays = 365;
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/" + lessonId + "/unarchive", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/enumerate", other.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(LibraryTestKit.Ids(list).Contains(lessonId), "Unarchived lessons return");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/series/" + seriesId + "/archive", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement seriesList = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/series/enumerate", other.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!LibraryTestKit.Ids(seriesList).Contains(seriesId), "Archived series leave lists");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/series/" + seriesId, watcher.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/series/" + seriesId, other.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task SlugsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string seriesId = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Fundamentals", ct, new { slug = "fundamentals" }).ConfigureAwait(false), "id");
            ApiResult duplicate = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/series", admin.AccessToken, new { title = "Other", slug = "fundamentals" }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Conflict, duplicate.Status, "Duplicate series slug");
            Check.Equal("SlugInUse", duplicate.Error, "SlugInUse");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/series", admin.AccessToken, new { title = "Bad slug", slug = "Not A Slug" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/series/" + seriesId, admin.AccessToken, new { slug = "basics" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement redirected = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/series/fundamentals", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(seriesId, Api.Str(redirected, "id"), "The old slug redirects");
            Check.Equal("basics", Api.Str(redirected, "slug"), "Current slug");
            string lessonId = Api.Str(await LibraryTestKit.LessonAsync(f, admin.AccessToken, seriesId, "Stance", ct, new { slug = "stance" }).ConfigureAwait(false), "id");
            ApiResult lessonDup = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/series/" + seriesId + "/lessons", admin.AccessToken, new { title = "Stance again", slug = "stance", kind = "Article" }, ct).ConfigureAwait(false);
            Check.Equal("SlugInUse", lessonDup.Error, "Lesson slugs are unique per series");
            string otherSeries = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Another", ct).ConfigureAwait(false), "id");
            await LibraryTestKit.LessonAsync(f, admin.AccessToken, otherSeries, "Stance elsewhere", ct, new { slug = "stance-two" }).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/lessons/" + lessonId, admin.AccessToken, new { slug = "base-stance" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement byOld = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/stance", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(lessonId, Api.Str(byOld, "id"), "Old lesson slug redirects");
            JsonElement byNew = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/base-stance", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(lessonId, Api.Str(byNew, "id"), "New lesson slug");
        }

        private static async Task LevelsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement settings = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/library/settings", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("None", Api.Str(settings, "levelSource"), "Engine default level source");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/library/levels", admin.AccessToken, new { key = "beginner", label = "Beginner" }, HttpStatusCode.Conflict, "LevelSourceNotCustom", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/library/settings", admin.AccessToken, new { levelSource = "Custom" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement beginner = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/library/levels", admin.AccessToken, new { key = "beginner", label = "Beginner", color = "#123456" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/library/levels", admin.AccessToken, new { key = "advanced", label = "Advanced" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/library/levels", admin.AccessToken, new { key = "beginner", label = "Again" }, HttpStatusCode.Conflict, "Conflict", ct).ConfigureAwait(false);
            JsonElement levels = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/library/levels/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(levels.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "key")).SequenceEqual(new[] { "beginner", "advanced" }), "Levels in order");
            JsonElement firstPage = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/library/levels/enumerate", member.AccessToken, new { maxResults = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!firstPage.GetProperty("endOfResults").GetBoolean() && !String.IsNullOrEmpty(Api.Str(firstPage, "continuationToken")), "Level page supplies continuation");
            JsonElement nextPage = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/library/levels/enumerate", member.AccessToken, new { maxResults = 1, continuationToken = Api.Str(firstPage, "continuationToken") }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("advanced", Api.Str(nextPage.GetProperty("objects")[0], "key"), "Level cursor advances");
            Check.True(nextPage.GetProperty("endOfResults").GetBoolean() && nextPage.GetProperty("continuationToken").ValueKind == JsonValueKind.Null, "Final level page has no cursor");
            string beginnerId = Api.Str(beginner, "id");
            string advancedId = Api.Str(levels.GetProperty("objects")[1], "id");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/LibraryLevel/" + beginnerId, admin.AccessToken, new { labels = new[] { "alpha" }, tags = new { topic = "basics" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/LibraryLevel/" + advancedId, admin.AccessToken, new { labels = new[] { "beta" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement any = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/library/levels/enumerate", member.AccessToken, new { labels = new[] { "alpha", "beta" }, labelMatch = "Any" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, any.GetProperty("objects").GetArrayLength(), "Any metadata match");
            JsonElement tagged = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/library/levels/enumerate", member.AccessToken, new { tags = new { topic = "basics" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("beginner", Api.Str(tagged.GetProperty("objects")[0], "key"), "Tag filter");
            JsonElement alphabetical = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/library/levels/enumerate", member.AccessToken, new { ordering = "LabelAscending", maxResults = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("advanced", Api.Str(alphabetical.GetProperty("objects")[0], "key"), "Requested ordering applies");
            await f.Raw.ExecuteQueryAsync("UPDATE librarylevels SET createdutc = '2024-01-01T00:00:00Z' WHERE id = '" + beginnerId + "'; UPDATE librarylevels SET createdutc = '2024-02-01T00:00:00Z' WHERE id = '" + advancedId + "';", false, ct).ConfigureAwait(false);
            JsonElement dated = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/library/levels/enumerate", member.AccessToken, new { createdAfterUtc = "2024-01-01T00:00:00Z", createdBeforeUtc = "2024-02-01T00:00:00Z" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, dated.GetProperty("objects").GetArrayLength(), "Creation window uses actual level timestamp");
            f.Settings.Enumeration.MaxResultsMember = 1;
            f.Settings.Enumeration.CountMemberTotals = false;
            JsonElement capped = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/library/levels/enumerate", member.AccessToken, new { maxResults = 500 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, capped.GetProperty("objects").GetArrayLength(), "Member bound clamps requested size");
            Check.True(capped.GetProperty("totalRecords").ValueKind == JsonValueKind.Null, "Member count setting respected");
            f.Settings.Enumeration.MaxResultsMember = 100;
            f.Settings.Enumeration.CountMemberTotals = true;
            ApiResult unknown = await Api.SendAsync(f, HttpMethod.Post, "/api/v1/admin/series", admin.AccessToken, new { title = "Leveled", levels = new[] { "expert" } }, ct).ConfigureAwait(false);
            Check.Equal("UnknownOption", unknown.Error, "Unknown level key");
            JsonElement series = await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Leveled", ct, new { levels = new[] { "beginner" } }).ConfigureAwait(false);
            Check.Equal("Beginner", Api.Str(series.GetProperty("levels")[0], "label"), "Level labels render");
            JsonElement filtered = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/series/enumerate", member.AccessToken, new { filters = new { levels = new[] { "beginner" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(LibraryTestKit.Ids(filtered).SequenceEqual(new[] { Api.Str(series, "id") }), "Level filter");
            JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement library = property.GetProperty("library");
            Check.Equal("Custom", Api.Str(library, "levelSource"), "Property document carries the database copy");
            Check.Equal(2, library.GetProperty("levels").GetArrayLength(), "Property document levels");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/library/levels/" + Api.Str(beginner, "id"), admin.AccessToken, new { label = "Starter" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/library/settings", member.AccessToken, new { levelSource = "None" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/library/settings", admin.AccessToken, new { levelSource = "ProfileField", levelFieldKey = "nope" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/library/settings", admin.AccessToken, new { levelSource = "ProfileField", levelFieldKey = "rank" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement rank = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/library/levels/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ProfileField field = (await f.Services.ProfileFields.AllAsync(ct).ConfigureAwait(false)).First(p => p.Key == "rank");
            Check.True(rank.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "key")).SequenceEqual(field.Options.Where(o => !o.Retired).Select(o => o.Key)), "ProfileField levels follow the field's options");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "library.level.create", Api.Str(beginner, "id"), ct).ConfigureAwait(false) >= 1, "Level create audited");
        }

        private static async Task EditorAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember editor = await LibraryTestKit.EditorAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement series = await LibraryTestKit.SeriesAsync(f, editor.AccessToken, "Editor course", ct).ConfigureAwait(false);
            string seriesId = Api.Str(series, "id");
            JsonElement lesson = await LibraryTestKit.LessonAsync(f, editor.AccessToken, seriesId, "Editor lesson", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/lessons/" + Api.Str(lesson, "id"), editor.AccessToken, new { summary = "Edited by an editor" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/enumerate", editor.AccessToken, new { filters = new { seriesId } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/library/settings", editor.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/library/settings", editor.AccessToken, new { downloadsEnabled = true }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            ApiResult publicSeries = await Api.SendAsync(f, HttpMethod.Put, "/api/v1/admin/series/" + seriesId, editor.AccessToken, new { audience = "Public" }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.BadRequest, publicSeries.Status, "Only SystemAdmin sets Public");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/series", member.AccessToken, new { title = "Member series" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/enumerate", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            byte[] pdf = DocumentKit.Pdf("Editor notes");
            string doc = await MediaTestKit.UploadSingleAsync(f, editor.AccessToken, "Document", "application/pdf", pdf, ct).ConfigureAwait(false);
            Check.True(doc.Length > 0, "Library editors upload documents");
            MediaHttpResult denied = await MediaTestKit.JsonAsync(f, HttpMethod.Post, "/api/v1/media/uploads", member.AccessToken, new { kind = "Document", contentType = "application/pdf", sizeBytes = pdf.Length }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Forbidden, denied.Status, "Members cannot upload documents");
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + Api.Str(lesson, "id"), editor.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(detail.GetProperty("viewer").GetProperty("canEdit").GetBoolean(), "Editors see canEdit");
            JsonElement asMember = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + Api.Str(lesson, "id"), member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!asMember.GetProperty("viewer").GetProperty("canEdit").GetBoolean(), "Members do not");
        }

        private static async Task NotificationsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember follower = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember quitter = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string seriesId = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Followed course", ct).ConfigureAwait(false), "id");
            JsonElement followed = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/series/" + seriesId + "/follow", follower.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(followed.GetProperty("isFollowed").GetBoolean(), "Followed");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/series/" + seriesId + "/follow", quitter.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/series/" + seriesId + "/follow", quitter.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement series = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/series/" + seriesId, follower.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(series.GetProperty("isFollowed").GetBoolean(), "isFollowed");
            await LibraryTestKit.LessonAsync(f, admin.AccessToken, seriesId, "Week one", ct).ConfigureAwait(false);
            await LibraryTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            List<Notification> first = await ForumTestKit.NotificationsAsync(f, follower.Id, NotificationTypeEnum.NewLessonInSeries, ct).ConfigureAwait(false);
            Check.Equal(1, first.Count, "One NewLessonInSeries");
            Check.True(first[0].Title.StartsWith("New lesson in Followed course", StringComparison.Ordinal), "Single title (" + first[0].Title + ")");
            Check.Equal(0, (await ForumTestKit.NotificationsAsync(f, quitter.Id, NotificationTypeEnum.NewLessonInSeries, ct).ConfigureAwait(false)).Count, "Unfollowed members get nothing");
            await LibraryTestKit.LessonAsync(f, admin.AccessToken, seriesId, "Week two", ct).ConfigureAwait(false);
            await LibraryTestKit.FanoutAsync(f, ct).ConfigureAwait(false);
            List<Notification> second = await ForumTestKit.NotificationsAsync(f, follower.Id, NotificationTypeEnum.NewLessonInSeries, ct).ConfigureAwait(false);
            Check.Equal(1, second.Count, "Aggregated while unread");
            Check.Equal("2 new lessons in Followed course", second[0].Title, "Aggregated title");
            Check.Equal("/series/" + seriesId, second[0].Path, "Aggregated path");
            string noisy = Api.Str(await LibraryTestKit.LessonAsync(f, admin.AccessToken, seriesId, "Notify staff", ct, new { notifyStaffOnComment = true }).ConfigureAwait(false), "id");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/" + noisy + "/comments", follower.AccessToken, new { body = "A question about this" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            List<Notification> staff = await ForumTestKit.NotificationsAsync(f, admin.Id, NotificationTypeEnum.PostComment, ct).ConfigureAwait(false);
            Check.True(staff.Any(n => n.TargetId == noisy), "Staff receive PostComment for notifyStaffOnComment lessons");
        }

        #endregion
    }
}
