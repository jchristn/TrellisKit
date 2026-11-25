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
    using TrellisKit.Server.Services;

    /// <summary>
    /// Unified library search (REST_API.md, Library search): per-kind bounds and continuation through each kind's own
    /// route, access filtering (drafts, locked stubs, signed out), the catalog switch, labels and tags, validation of short
    /// and long queries, the shared search rate limit, the library switch, the trigram indexes, and property isolation.
    /// </summary>
    public static class LibrarySearchSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "LibrarySearch";

        #endregion

        #region Private-Members

        private const string _Search = TrainingTestKit.Search;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Library search (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Routes.Declared", "Search is a declared, anonymous-eligible Member route under the library switch", ct =>
                {
                    Check.True(AccessRoutes.Find("POST", _Search)?.AnonymousEligible == true, "Declared and anonymous-eligible");
                    Check.True(AccessRoutes.Find("PUT", _Search) == null, "Only POST is declared");
                    Check.True(KillSwitchCatalog.FeaturesFor("POST", _Search).Contains("library"), "Library switch");
                    return Task.CompletedTask;
                }),
                Case("Search.PerKindBounds", "Each kind is bounded by maxResultsPerKind and continues through its own route; kinds not requested are empty", BoundsAsync),
                Case("Search.AccessFiltering", "Drafts never appear; free accounts get locked stubs; signed out only Public lessons open; techniques are published entries only", AccessAsync),
                Case("Search.Validation", "Queries of 2 to 100 characters, 1 to 25 per kind, known kinds only; otherwise 400 ValidationFailed", ValidationAsync),
                Case("Search.RateLimit", "The shared search limiter answers 429 RateLimited", RateLimitAsync),
                Case("Search.LabelsAndTags", "Labels and tags filter every kind", LabelsAsync),
                SuiteCases.Http(Id, "Search.CatalogOff", "features.catalog false: techniques stay empty even when requested", CatalogOffAsync),
                SuiteCases.Http(Id, "Search.LibraryOff", "features.library false: 404 NotAvailable", p => p.Features.Library = false, LibraryOffAsync),
                SuiteCases.Http(Id, "Search.TrigramIndexes", "With pg_trgm installed, migration 135 adds trigram indexes on the series and lesson search columns", IndexesAsync),
                SuiteCases.Http(Id, "Search.PropertyIsolation", "A member of another property finds none of this property's courses or lessons", MultiPropertyKit.Multi, PropertyIsolationAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body)
        {
            return CatalogTestKit.Http(Id, caseId, name, async (f, ct) =>
            {
                TrainingTestKit.Relax(f);
                await EventTestKit.PartiallyGatedAsync(f, ct, "library").ConfigureAwait(false);
                await body(f, ct).ConfigureAwait(false);
            });
        }

        private static string Marker()
        {
            return "Zq" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static async Task BoundsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string marker = Marker();
            JsonElement series = await LibraryTestKit.SeriesAsync(f, admin.AccessToken, marker + " course", ct).ConfigureAwait(false);
            await LibraryTestKit.SeriesAsync(f, admin.AccessToken, marker + " second course", ct).ConfigureAwait(false);
            for (int i = 0; i < 3; i++) await LibraryTestKit.LessonAsync(f, admin.AccessToken, Api.Str(series, "id"), marker + " lesson " + i, ct).ConfigureAwait(false);
            await TrainingTestKit.PublishedEntryAsync(f, marker + " movement", ct).ConfigureAwait(false);
            await TrainingTestKit.PublishedEntryAsync(f, marker + " second movement", ct).ConfigureAwait(false);

            JsonElement all = await Api.ExpectAsync(f, HttpMethod.Post, _Search, member.AccessToken, new { query = marker }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2, TrainingTestKit.Count(all.GetProperty("series")), "Series");
            Check.Equal(3, TrainingTestKit.Count(all.GetProperty("lessons")), "Lessons");
            Check.Equal(2, TrainingTestKit.Count(all.GetProperty("techniques")), "Techniques");
            Check.Equal(10, all.GetProperty("lessons").GetProperty("maxResults").GetInt32(), "Default 10 per kind");

            JsonElement one = await Api.ExpectAsync(f, HttpMethod.Post, _Search, member.AccessToken, new { query = marker, maxResultsPerKind = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            foreach (string kind in new[] { "series", "lessons", "techniques" })
            {
                Check.Equal(1, TrainingTestKit.Count(one.GetProperty(kind)), kind + " bounded by maxResultsPerKind");
                Check.True(!one.GetProperty(kind).GetProperty("endOfResults").GetBoolean(), kind + " has more");
            }
            JsonElement more = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/enumerate", member.AccessToken,
                new { search = marker, maxResults = 1, continuationToken = Api.Str(one.GetProperty("lessons"), "continuationToken") }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, TrainingTestKit.Count(more), "The lessons page continues through /lessons/enumerate");
            Check.True(TrainingTestKit.Ids(more)[0] != TrainingTestKit.Ids(one.GetProperty("lessons"))[0], "The next lesson");

            JsonElement lessonsOnly = await Api.ExpectAsync(f, HttpMethod.Post, _Search, member.AccessToken, new { query = marker, kinds = new[] { "lesson" }, maxResultsPerKind = 25 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(3, TrainingTestKit.Count(lessonsOnly.GetProperty("lessons")), "Kinds are case-insensitive");
            Check.Equal(0, TrainingTestKit.Count(lessonsOnly.GetProperty("series")), "Series not requested");
            Check.Equal(0, TrainingTestKit.Count(lessonsOnly.GetProperty("techniques")), "Techniques not requested");
            Check.True(lessonsOnly.GetProperty("series").GetProperty("endOfResults").GetBoolean(), "Empty pages are terminal");
        }

        private static async Task AccessAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            string marker = Marker();
            JsonElement series = await LibraryTestKit.SeriesAsync(f, admin.AccessToken, marker + " course", ct).ConfigureAwait(false);
            string publicLesson = Api.Str(await LibraryTestKit.LessonAsync(f, admin.AccessToken, Api.Str(series, "id"), marker + " public", ct, new { audience = "Public" }).ConfigureAwait(false), "id");
            string membersLesson = Api.Str(await LibraryTestKit.LessonAsync(f, admin.AccessToken, Api.Str(series, "id"), marker + " members", ct, new { audience = "Members" }).ConfigureAwait(false), "id");
            string draftLesson = Api.Str(await LibraryTestKit.LessonAsync(f, admin.AccessToken, Api.Str(series, "id"), marker + " draft lesson", ct, null, false).ConfigureAwait(false), "id");
            JsonElement draftSeries = await LibraryTestKit.SeriesAsync(f, admin.AccessToken, marker + " draft", ct, null, false).ConfigureAwait(false);
            string entry = await TrainingTestKit.PublishedEntryAsync(f, marker + " movement", ct).ConfigureAwait(false);
            await TrainingTestKit.DraftEntryAsync(f, marker + " unreviewed", ct).ConfigureAwait(false);

            JsonElement result = await Api.ExpectAsync(f, HttpMethod.Post, _Search, member.AccessToken, new { query = marker }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(TrainingTestKit.Ids(result.GetProperty("series")).SequenceEqual(new[] { Api.Str(series, "id") }), "Published series only");
            Check.True(!TrainingTestKit.Ids(result.GetProperty("series")).Contains(Api.Str(draftSeries, "id")), "Draft series hidden");
            List<string> lessons = TrainingTestKit.Ids(result.GetProperty("lessons"));
            Check.True(lessons.Contains(publicLesson) && lessons.Contains(membersLesson) && !lessons.Contains(draftLesson), "Members find published lessons only");
            Check.True(TrainingTestKit.Ids(result.GetProperty("techniques")).SequenceEqual(new[] { entry }), "Published techniques only");

            JsonElement freeResult = await Api.ExpectAsync(f, HttpMethod.Post, _Search, free.AccessToken, new { query = marker, kinds = new[] { "Lesson" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement stub = freeResult.GetProperty("lessons").GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "id") == membersLesson);
            Check.True(stub.GetProperty("locked").GetBoolean(), "Free accounts see Members lessons as locked stubs");

            JsonElement guest = await Api.ExpectAsync(f, HttpMethod.Post, _Search, null, new { query = marker }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> open = guest.GetProperty("lessons").GetProperty("objects").EnumerateArray().Where(o => !o.GetProperty("locked").GetBoolean()).Select(o => Api.Str(o, "id")).ToList();
            Check.True(open.SequenceEqual(new[] { publicLesson }), "Signed out, only the Public lesson opens");
            await EventTestKit.SetAccessAsync(f, new { model = "PartiallyGated", anonymousBrowsing = false, gatedFeatures = new[] { "library" }, defaultAudience = "SignedIn" }, ct).ConfigureAwait(false);
            ApiResult closed = await Api.SendAsync(f, HttpMethod.Post, _Search, null, new { query = marker }, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.Unauthorized, closed.Status, "Signed out needs anonymous browsing");
        }

        private static async Task ValidationAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            foreach (object body in new object[]
            {
                new { },
                new { query = "a" },
                new { query = "  a  " },
                new { query = new string('q', 101) },
                new { query = "valid", maxResultsPerKind = 0 },
                new { query = "valid", maxResultsPerKind = 26 },
                new { query = "valid", kinds = new[] { "Venue" } },
                new { query = "valid", kinds = new[] { "Series", "Lesson", "CatalogEntry", "Series" } }
            })
                await Api.ExpectErrorAsync(f, HttpMethod.Post, _Search, member.AccessToken, body, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _Search, member.AccessToken, new { query = "ab" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _Search, member.AccessToken, new { query = new string('q', 100), maxResultsPerKind = 25 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task RateLimitAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            f.Settings.Search.PerMinute = 1;
            await Api.ExpectAsync(f, HttpMethod.Post, _Search, member.AccessToken, new { query = "limit" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Search, member.AccessToken, new { query = "limit" }, HttpStatusCode.TooManyRequests, "RateLimited", ct).ConfigureAwait(false);
            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _Search, other.AccessToken, new { query = "limit" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task LabelsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string marker = Marker();
            JsonElement tagged = await LibraryTestKit.SeriesAsync(f, admin.AccessToken, marker + " tagged course", ct, new { labels = new[] { "fundamentals" }, tags = new Dictionary<string, string> { { "level", "one" } } }).ConfigureAwait(false);
            await LibraryTestKit.SeriesAsync(f, admin.AccessToken, marker + " plain course", ct).ConfigureAwait(false);
            JsonElement byLabel = await Api.ExpectAsync(f, HttpMethod.Post, _Search, member.AccessToken, new { query = marker, kinds = new[] { "Series" }, labels = new[] { "fundamentals" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(TrainingTestKit.Ids(byLabel.GetProperty("series")).SequenceEqual(new[] { Api.Str(tagged, "id") }), "Label filter");
            JsonElement byTag = await Api.ExpectAsync(f, HttpMethod.Post, _Search, member.AccessToken, new { query = marker, kinds = new[] { "Series" }, tags = new Dictionary<string, string> { { "level", "one" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(TrainingTestKit.Ids(byTag.GetProperty("series")).SequenceEqual(new[] { Api.Str(tagged, "id") }), "Tag filter");
        }

        private static async Task CatalogOffAsync(HttpFixture f, CancellationToken ct)
        {
            TrainingTestKit.Relax(f);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string marker = Marker();
            await TrainingTestKit.LessonAsync(f, admin, marker + " lesson", ct).ConfigureAwait(false);
            JsonElement result = await Api.ExpectAsync(f, HttpMethod.Post, _Search, member.AccessToken, new { query = marker, kinds = new[] { "Lesson", "CatalogEntry" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, TrainingTestKit.Count(result.GetProperty("lessons")), "Lessons found");
            Check.Equal(0, TrainingTestKit.Count(result.GetProperty("techniques")), "No techniques while the catalog is off");
        }

        private static async Task LibraryOffAsync(HttpFixture f, CancellationToken ct)
        {
            TrainingTestKit.Relax(f);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Search, member.AccessToken, new { query = "anything" }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
        }

        private static async Task IndexesAsync(HttpFixture f, CancellationToken ct)
        {
            long trgm = await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM pg_extension WHERE extname = 'pg_trgm';", ct).ConfigureAwait(false);
            long indexes = await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM pg_indexes WHERE schemaname = current_schema() AND indexname IN ('idx_series_title_trgm', 'idx_series_summary_trgm', 'idx_series_description_trgm', 'idx_lessons_title_trgm', 'idx_lessons_summary_trgm', 'idx_lessons_description_trgm');", ct).ConfigureAwait(false);
            Check.Equal(trgm == 0 ? 0L : 6L, indexes, "Trigram indexes follow the extension (pg_trgm installed: " + trgm + ")");
            long covering = await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM pg_indexes WHERE schemaname = current_schema() AND indexname = 'idx_lessons_series_published';", ct).ConfigureAwait(false);
            Check.Equal(1L, covering, "New for you covering index");
        }

        private static async Task PropertyIsolationAsync(HttpFixture f, CancellationToken ct)
        {
            TrainingTestKit.Relax(f);
            string platform = await MultiPropertyKit.PlatformLoginAsync(f, ct).ConfigureAwait(false);
            string b = await MultiPropertyKit.CreateSecondAsync(f, platform, ct).ConfigureAwait(false);
            f.PropertyId = TestProperty.SampleId;
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember inA = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string marker = Marker();
            await TrainingTestKit.LessonAsync(f, admin, marker + " lesson", ct, new { audience = "Public" }).ConfigureAwait(false);
            JsonElement a = await Api.ExpectAsync(f, HttpMethod.Post, _Search, inA.AccessToken, new { query = marker }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, TrainingTestKit.Count(a.GetProperty("lessons")), "A finds its lesson");
            f.PropertyId = b;
            TestMember inB = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            JsonElement other = await Api.ExpectAsync(f, HttpMethod.Post, _Search, inB.AccessToken, new { query = marker }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, TrainingTestKit.Count(other.GetProperty("lessons")) + TrainingTestKit.Count(other.GetProperty("series")), "B finds nothing of A");
            JsonElement guest = await Api.ExpectAsync(f, HttpMethod.Post, _Search, null, new { query = marker }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, TrainingTestKit.Count(guest.GetProperty("lessons")), "B's guests find nothing of A");
            f.PropertyId = TestProperty.SampleId;
        }

        #endregion
    }
}
