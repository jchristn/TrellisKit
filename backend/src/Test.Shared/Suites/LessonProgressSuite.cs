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
    /// Lesson progress (docs/PHASE4_CONTRACT.md section 5.5 and resolved decision 17): anti-skip clamping, stale and
    /// replayed updates, offline out-of-order replay, the completion threshold and sticky completion, the resume tail,
    /// concurrent updates, series progress, Continue watching, automatic follows, audited staff viewing history, and
    /// library metrics.
    /// </summary>
    public static class LessonProgressSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "LessonProgress";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Lesson progress (" + TestEnvironment.Provider + ")", new[]
            {
                Case("Progress.AntiSkip", "playedSeconds is clamped to twice the interval and positions to the duration, so seeking to the end does not complete a lesson", AntiSkipAsync),
                Case("Progress.StaleAndOffline", "Older clientUpdatedUtc keeps the resume point while furthest position and watched time still merge; an exact replay changes nothing; entries older than the offline window are dropped", StaleAsync),
                Case("Progress.CompletionSticky", "Completion at the threshold is sticky across later updates; uncomplete clears it; complete marks it manually; completion auto-follows the series", CompletionAsync),
                Case("Progress.ResumeTail", "A position inside the resume tail resumes from 0", ResumeTailAsync),
                Case("Progress.Concurrent", "Concurrent updates from one member sum watched time exactly and keep the furthest position", ConcurrentAsync),
                Case("Progress.SeriesAndContinue", "Series myProgress comes from one grouped query; Continue watching lists in-progress lessons newest first; my progress filters by completed and series; reset deletes progress", SeriesAndContinueAsync),
                Case("Progress.ViewingHistory", "Library staff read a member's viewing history and a lesson's viewers; every read is audited; members get 403; metrics are aggregates", ViewingHistoryAsync)
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

        private static async Task<string> LessonAsync(HttpFixture f, TestMember admin, double seconds, CancellationToken ct, string title = "Tracked lesson")
        {
            string series = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, title + " series", ct).ConfigureAwait(false), "id");
            return await LibraryTestKit.VideoLessonAsync(f, admin, series, title, seconds, ct).ConfigureAwait(false);
        }

        private static async Task AntiSkipAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await LessonAsync(f, admin, 300, ct).ConfigureAwait(false);
            JsonElement p = await LibraryTestKit.ProgressAsync(f, member.AccessToken, lesson, 5000, 1000, DateTime.UtcNow, ct).ConfigureAwait(false);
            Check.Equal(300.0, p.GetProperty("maxPositionSeconds").GetDouble(), "Position clamped to the duration");
            Check.Equal(30.0, p.GetProperty("watchedSeconds").GetDouble(), "playedSeconds clamped to 2 x 15");
            Check.Equal(10, p.GetProperty("percent").GetInt32(), "percent uses min(furthest, watched)");
            Check.True(!p.GetProperty("completed").GetBoolean(), "Skipping to the end does not complete");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/lessons/" + lesson + "/progress", member.AccessToken, new { playedSeconds = 1 }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/lessons/" + lesson + "/progress", member.AccessToken, new { positionSeconds = -1 }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        private static async Task StaleAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await LessonAsync(f, admin, 1000, ct).ConfigureAwait(false);
            DateTime t0 = DateTime.UtcNow.AddHours(-3);
            await LibraryTestKit.ProgressAsync(f, member.AccessToken, lesson, 200, 10, t0.AddMinutes(20), ct).ConfigureAwait(false);
            JsonElement old = await LibraryTestKit.ProgressAsync(f, member.AccessToken, lesson, 50, 10, t0.AddMinutes(10), ct).ConfigureAwait(false);
            Check.Equal(200.0, old.GetProperty("positionSeconds").GetDouble(), "An older update keeps the resume point");
            Check.Equal(20.0, old.GetProperty("watchedSeconds").GetDouble(), "Watched time merges regardless");
            JsonElement offline = await LibraryTestKit.ProgressAsync(f, member.AccessToken, lesson, 400, 10, t0.AddMinutes(5), ct).ConfigureAwait(false);
            Check.Equal(400.0, offline.GetProperty("maxPositionSeconds").GetDouble(), "Furthest position merges monotonically from an out-of-order replay");
            Check.Equal(200.0, offline.GetProperty("positionSeconds").GetDouble(), "Resume point unchanged by the replay");
            JsonElement replay = await LibraryTestKit.ProgressAsync(f, member.AccessToken, lesson, 200, 10, t0.AddMinutes(20), ct).ConfigureAwait(false);
            Check.Equal(30.0, replay.GetProperty("watchedSeconds").GetDouble(), "An exact replay is not counted twice");
            JsonElement newer = await LibraryTestKit.ProgressAsync(f, member.AccessToken, lesson, 220, 10, t0.AddMinutes(30), ct).ConfigureAwait(false);
            Check.Equal(220.0, newer.GetProperty("positionSeconds").GetDouble(), "A newer update moves the resume point");
            JsonElement ancient = await LibraryTestKit.ProgressAsync(f, member.AccessToken, lesson, 999, 30, DateTime.UtcNow.AddDays(-8), ct).ConfigureAwait(false);
            Check.Equal(40.0, ancient.GetProperty("watchedSeconds").GetDouble(), "Entries older than 7 days are dropped");
            Check.Equal(400.0, ancient.GetProperty("maxPositionSeconds").GetDouble(), "Dropped entries change nothing");
        }

        private static async Task CompletionAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await LessonAsync(f, admin, 100, ct).ConfigureAwait(false);
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + lesson, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string seriesId = Api.Str(detail.GetProperty("series"), "id");
            DateTime t = DateTime.UtcNow.AddMinutes(-30);
            JsonElement p = default;
            for (int i = 1; i <= 3; i++) p = await LibraryTestKit.ProgressAsync(f, member.AccessToken, lesson, i * 30, 25, t.AddSeconds(i * 30), ct).ConfigureAwait(false);
            Check.True(!p.GetProperty("completed").GetBoolean(), "75 seconds watched is below the threshold even at position 90");
            p = await LibraryTestKit.ProgressAsync(f, member.AccessToken, lesson, 92, 20, t.AddSeconds(120), ct).ConfigureAwait(false);
            Check.True(p.GetProperty("completed").GetBoolean(), "Completed at the threshold");
            p = await LibraryTestKit.ProgressAsync(f, member.AccessToken, lesson, 5, 1, t.AddSeconds(200), ct).ConfigureAwait(false);
            Check.True(p.GetProperty("completed").GetBoolean(), "Completion is sticky");
            JsonElement series = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/series/" + seriesId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(series.GetProperty("isFollowed").GetBoolean(), "Completion auto-follows the series");
            JsonElement un = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/" + lesson + "/uncomplete", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!un.GetProperty("completed").GetBoolean(), "uncomplete clears");
            JsonElement manual = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/" + lesson + "/complete", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(manual.GetProperty("completed").GetBoolean() && manual.TryGetProperty("completedUtc", out JsonElement when) && when.ValueKind == JsonValueKind.String, "Manual completion");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/series/" + seriesId + "/follow", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/" + lesson + "/uncomplete", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/" + lesson + "/complete", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            series = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/series/" + seriesId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!series.GetProperty("isFollowed").GetBoolean(), "An unfollow is not undone by an automatic follow");
        }

        private static async Task ResumeTailAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await LessonAsync(f, admin, 200, ct).ConfigureAwait(false);
            JsonElement p = await LibraryTestKit.ProgressAsync(f, member.AccessToken, lesson, 195, 10, DateTime.UtcNow, ct).ConfigureAwait(false);
            Check.Equal(0.0, p.GetProperty("positionSeconds").GetDouble(), "Inside the last 10 seconds resumes from 0");
            Check.Equal(195.0, p.GetProperty("maxPositionSeconds").GetDouble(), "Furthest position kept");
            p = await LibraryTestKit.ProgressAsync(f, member.AccessToken, lesson, 150, 10, DateTime.UtcNow.AddSeconds(1), ct).ConfigureAwait(false);
            Check.Equal(150.0, p.GetProperty("positionSeconds").GetDouble(), "Outside the tail resumes there");
        }

        private static async Task ConcurrentAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await LessonAsync(f, admin, 2000, ct).ConfigureAwait(false);
            DateTime t = DateTime.UtcNow.AddMinutes(-20);
            List<Task<JsonElement>> tasks = Enumerable.Range(1, 12).Select(i => LibraryTestKit.ProgressAsync(f, member.AccessToken, lesson, i * 10, 7, t.AddSeconds(i), ct, "s1")).ToList();
            await Task.WhenAll(tasks).ConfigureAwait(false);
            JsonElement mine = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/library/progress/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement row = mine.GetProperty("objects")[0];
            Check.Equal(84.0, row.GetProperty("watchedSeconds").GetDouble(), "Watched time sums exactly");
            Check.Equal(120.0, row.GetProperty("maxPositionSeconds").GetDouble(), "Furthest position");
            Check.Equal(120.0, row.GetProperty("positionSeconds").GetDouble(), "Resume point from the newest client time");
            long plays = await LibraryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM lessonplays WHERE lessonid = '" + lesson + "';", ct).ConfigureAwait(false);
            Check.Equal(1L, plays, "One play per session");
            long rows = await LibraryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM lessonprogress WHERE lessonid = '" + lesson + "';", ct).ConfigureAwait(false);
            Check.Equal(1L, rows, "One progress row");
        }

        private static async Task SeriesAndContinueAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string seriesId = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Progress series", ct).ConfigureAwait(false), "id");
            string a = await LibraryTestKit.VideoLessonAsync(f, admin, seriesId, "Part one", 100, ct).ConfigureAwait(false);
            string b = await LibraryTestKit.VideoLessonAsync(f, admin, seriesId, "Part two", 100, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/" + a + "/complete", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await LibraryTestKit.ProgressAsync(f, member.AccessToken, b, 40, 20, DateTime.UtcNow, ct).ConfigureAwait(false);
            JsonElement series = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/series/" + seriesId, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement mp = series.GetProperty("myProgress");
            Check.Equal(1, mp.GetProperty("completedLessons").GetInt32(), "Completed lessons");
            Check.Equal(50, mp.GetProperty("percent").GetInt32(), "Series percent");
            Check.Equal(b, Api.Str(mp, "lastLessonId"), "Last watched lesson");
            JsonElement inProgress = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/series/enumerate", member.AccessToken, new { filters = new { progress = "InProgress" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(LibraryTestKit.Ids(inProgress).Contains(seriesId), "Series progress filter");
            JsonElement cont = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/library/continue/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> continued = cont.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o.GetProperty("lesson"), "id")).ToList();
            Check.True(continued.SequenceEqual(new[] { b }), "Continue watching excludes completed lessons");
            JsonElement done = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/library/progress/enumerate", member.AccessToken, new { filters = new { completed = true, seriesId } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(done.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "lessonId")).SequenceEqual(new[] { a }), "My progress filters");
            JsonElement lessons = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/enumerate", member.AccessToken, new { filters = new { progress = "NotStarted", seriesIds = new[] { seriesId } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, lessons.GetProperty("objects").GetArrayLength(), "Lesson progress filter");
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/lessons/" + b + "/progress", member.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            cont = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/me/library/continue/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, cont.GetProperty("objects").GetArrayLength(), "Reset removes it");
        }

        private static async Task ViewingHistoryAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember editor = await LibraryTestKit.EditorAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await LessonAsync(f, admin, 100, ct).ConfigureAwait(false);
            await LibraryTestKit.ProgressAsync(f, member.AccessToken, lesson, 30, 20, DateTime.UtcNow, ct, "abc").ConfigureAwait(false);
            JsonElement history = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/library/members/" + member.Id + "/progress/enumerate", editor.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement row = history.GetProperty("objects")[0];
            Check.Equal(lesson, Api.Str(row.GetProperty("lesson"), "id"), "History lists the lesson");
            Check.Equal(1L, row.GetProperty("plays").GetInt64(), "Plays per lesson");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "library.viewingHistory.read", member.Id, ct).ConfigureAwait(false) >= 1, "Member history read audited");
            JsonElement viewers = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/" + lesson + "/viewers/enumerate", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(member.Id, Api.Str(viewers.GetProperty("objects")[0].GetProperty("user"), "id"), "Viewers list the member");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "library.viewingHistory.read", lesson, ct).ConfigureAwait(false) >= 1, "Lesson viewers read audited");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/library/members/" + member.Id + "/progress/enumerate", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/" + lesson + "/viewers/enumerate", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement metrics = await MetricEnumerationTestKit.DailyAsync(f, "/api/v1/admin/metrics/library?days=30", editor.AccessToken, ct).ConfigureAwait(false);
            Check.Equal(30, metrics.GetProperty("playsByDay").GetArrayLength(), "One value per day");
            Check.Equal(1L, metrics.GetProperty("activeLearners").GetInt64(), "Active learners");
            Check.Equal(lesson, Api.Str(metrics.GetProperty("topLessons").GetProperty("objects")[0], "id"), "Top lessons");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/metrics/library?days=3", admin.AccessToken, null, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/admin/metrics/library", member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
        }

        #endregion
    }
}
