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
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Lesson and catalog links (REST_API.md, Lesson techniques): link limits and validation, the unpublished-entry
    /// conflict, access filtering in both directions (locked stubs for PartiallyGated free accounts), the public variant,
    /// archived lessons and unpublished entries dropping out, deletion of either side, labels and tags, the catalog feature
    /// switch, and property isolation.
    /// </summary>
    public static class LessonCatalogSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "LessonCatalog";

        #endregion

        #region Private-Members

        private const string _Admin = "/api/v1/admin/lessons/";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Lesson techniques (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "Routes.Declared", "Link routes are declared, follow the library and catalog switches, and the table is property-owned", ct =>
                {
                    Check.True(AccessRoutes.Find("GET", "/api/v1/lessons/les_x/catalog-entries")?.AnonymousEligible == true, "Lesson techniques declared");
                    Check.True(AccessRoutes.Find("POST", "/api/v1/catalog/entries/cex_x/lessons/enumerate") != null, "Lessons that teach this declared");
                    Check.True(KillSwitchCatalog.FeaturesFor("GET", "/api/v1/catalog/entries/cex_x/lessons").Contains("catalog"), "Catalog switch");
                    Check.True(KillSwitchCatalog.FeaturesFor("GET", "/api/v1/lessons/les_x/catalog-entries").Contains("library"), "Library switch");
                    Check.True(PropertyTables.IsOwned("lessoncatalogentries"), "Property-owned");
                    return Task.CompletedTask;
                }),
                Case("Links.LimitsAndValidation", "Only staff replace links; at most 50; each entry once; positions and offsets validated; unknown entries 404; drafts 409 CatalogEntryNotPublished; order, offsets, source, and labels round-trip; audited; [] clears", LimitsAsync),
                Case("Links.AccessFiltering", "Members see techniques in visible lessons and lessons under their access; free accounts see locked stubs; the public variant lists Public lessons only", AccessAsync),
                Case("Links.Lifecycle", "Archived lessons leave Lessons that teach this; unpublished entries leave Techniques in this lesson for members (staff see a warning); deleting a lesson or an entry removes its links", LifecycleAsync),
                Case("Links.Enumeration", "Pages are bounded at 50 with continuation; labels and tags filter links", EnumerationAsync),
                SuiteCases.Http(Id, "Links.CatalogOff", "features.catalog false: every link route is 404 NotAvailable", CatalogOffAsync),
                CatalogTestKit.Http(Id, "Links.PropertyIsolation", "Another property's driver reads, enumerates, and deletes none of this property's links", PropertyIsolationAsync)
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

        private static Task<JsonElement> SetAsync(HttpFixture f, string bearer, string lessonId, object items, CancellationToken ct)
        {
            return Api.ExpectAsync(f, HttpMethod.Put, _Admin + lessonId + "/catalog-entries", bearer, new { items }, HttpStatusCode.OK, ct);
        }

        private static async Task LimitsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember editor = await LibraryTestKit.EditorAsync(f, ct).ConfigureAwait(false);
            string lesson = await TrainingTestKit.LessonAsync(f, admin, "Linked lesson", ct).ConfigureAwait(false);
            string entry = await TrainingTestKit.PublishedEntryAsync(f, "Linked movement", ct).ConfigureAwait(false);
            string second = await TrainingTestKit.PublishedEntryAsync(f, "Second movement", ct).ConfigureAwait(false);
            string draft = await TrainingTestKit.DraftEntryAsync(f, "Draft movement", ct).ConfigureAwait(false);
            string path = _Admin + lesson + "/catalog-entries";

            await Api.ExpectErrorAsync(f, HttpMethod.Put, path, member.AccessToken, new { items = new[] { new { catalogEntryId = entry } } }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, path, admin.AccessToken, new { items = new[] { new { catalogEntryId = draft } } }, HttpStatusCode.Conflict, "CatalogEntryNotPublished", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, path, admin.AccessToken, new { items = new[] { new { catalogEntryId = "cex_none" } } }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Admin + "les_none/catalog-entries", admin.AccessToken, new { items = new[] { new { catalogEntryId = entry } } }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, path, admin.AccessToken, new { items = new[] { new { catalogEntryId = entry }, new { catalogEntryId = entry } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, path, admin.AccessToken, new { }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            object[] tooMany = Enumerable.Range(0, 51).Select(i => (object)new { catalogEntryId = "cex_" + i }).ToArray();
            await Api.ExpectErrorAsync(f, HttpMethod.Put, path, admin.AccessToken, new { items = tooMany }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, path, admin.AccessToken, new { items = new[] { new { catalogEntryId = entry, startSeconds = -1 } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, path, admin.AccessToken, new { items = new[] { new { catalogEntryId = entry, startSeconds = 86401 } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, path, admin.AccessToken, new { items = new[] { new { catalogEntryId = entry, position = 51 } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);

            JsonElement set = await SetAsync(f, editor.AccessToken, lesson, new object[]
            {
                new { catalogEntryId = second, position = 2 },
                new { catalogEntryId = entry, startSeconds = 42, position = 1, source = "Suggested", labels = new[] { "key" }, tags = new Dictionary<string, string> { { "focus", "setup" } } }
            }, ct).ConfigureAwait(false);
            List<JsonElement> links = set.GetProperty("objects").EnumerateArray().ToList();
            Check.True(links.Select(l => Api.Str(l, "id")).SequenceEqual(new[] { entry, second }), "Position order (a library editor may link)");
            Check.True(links.Select(l => l.GetProperty("position").GetInt32()).SequenceEqual(new[] { 1, 2 }), "Positions renumbered");
            Check.Equal(42, links[0].GetProperty("startSeconds").GetInt32(), "Start offset");
            Check.True(links[1].GetProperty("startSeconds").ValueKind == JsonValueKind.Null, "No offset");
            Check.True(Api.Str(links[0], "linkId").StartsWith("lce_", StringComparison.Ordinal), "Link id prefix");
            Check.Equal("Suggested", Api.Str(links[0], "source"), "Source");
            Check.Equal("Editor", Api.Str(links[1], "source"), "Default source");
            Check.Equal("Linked movement", Api.Str(links[0], "name"), "Entry view fields");
            Check.True(links[0].GetProperty("linkLabels").EnumerateArray().Any(l => l.GetString() == "key"), "Link labels");
            Check.Equal("setup", Api.Str(links[0].GetProperty("linkTags"), "focus"), "Link tags");
            Check.True(await ModerationTestKit.AuditCountAsync(f, "lesson.catalogEntries", lesson, ct).ConfigureAwait(false) >= 1, "Audited");

            object[] fifty = new object[50];
            List<string> entries = new List<string>();
            for (int i = 0; i < 50; i++)
            {
                string id = i == 0 ? entry : i == 1 ? second : await TrainingTestKit.PublishedEntryAsync(f, "Bulk movement " + i, ct).ConfigureAwait(false);
                entries.Add(id);
                fifty[i] = new { catalogEntryId = id };
            }
            JsonElement full = await SetAsync(f, admin.AccessToken, lesson, fifty, ct).ConfigureAwait(false);
            Check.Equal(50, TrainingTestKit.Count(full), "Exactly 50 links are accepted");
            Check.Equal(50, (await f.Database.LessonCatalogLinks.ListForLessonAsync(lesson, ct).ConfigureAwait(false)).Count, "50 stored");

            await SetAsync(f, admin.AccessToken, lesson, Array.Empty<object>(), ct).ConfigureAwait(false);
            Check.Equal(0, (await f.Database.LessonCatalogLinks.ListForLessonAsync(lesson, ct).ConfigureAwait(false)).Count, "An empty list removes every link");
            Check.Equal(0L, await LibraryTestKit.CountAsync(f, "SELECT COUNT(*) AS cnt FROM labels WHERE propertyid = '" + LibraryTestKit.Pid(f) + "' AND targettype = 'LessonCatalogEntry';", ct).ConfigureAwait(false), "Link labels removed with the links");
        }

        private static async Task AccessAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            string membersLesson = await TrainingTestKit.LessonAsync(f, admin, "Members lesson", ct, new { audience = "Members" }).ConfigureAwait(false);
            string publicLesson = await TrainingTestKit.LessonAsync(f, admin, "Public lesson", ct, new { audience = "Public" }).ConfigureAwait(false);
            string hiddenSeries = Api.Str(await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Unpublished course", ct, null, false).ConfigureAwait(false), "id");
            string unpublishedLesson = Api.Str(await LibraryTestKit.LessonAsync(f, admin.AccessToken, hiddenSeries, "Unpublished lesson", ct, null, false).ConfigureAwait(false), "id");
            string entry = await TrainingTestKit.PublishedEntryAsync(f, "Taught movement", ct).ConfigureAwait(false);
            foreach (string lesson in new[] { membersLesson, publicLesson, unpublishedLesson })
                await SetAsync(f, admin.AccessToken, lesson, new[] { new { catalogEntryId = entry } }, ct).ConfigureAwait(false);

            JsonElement techniques = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + membersLesson + "/catalog-entries", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(TrainingTestKit.Ids(techniques).SequenceEqual(new[] { entry }), "Members see the lesson's techniques");
            Check.True(techniques.GetProperty("objects")[0].GetProperty("warning").ValueKind == JsonValueKind.Null, "No warning for a published entry");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + membersLesson + "/catalog-entries", free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/lessons/" + unpublishedLesson + "/catalog-entries", member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/lessons/les_none/catalog-entries", member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);

            JsonElement teaching = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/catalog/entries/" + entry + "/lessons", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> taught = TrainingTestKit.Ids(teaching);
            Check.True(taught.Contains(membersLesson) && taught.Contains(publicLesson) && !taught.Contains(unpublishedLesson), "Members see published lessons that teach the technique");
            Check.True(teaching.GetProperty("objects").EnumerateArray().All(o => !o.GetProperty("locked").GetBoolean()), "Nothing locked for a member");
            JsonElement freeTeaching = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/catalog/entries/" + entry + "/lessons/enumerate", free.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement stub = freeTeaching.GetProperty("objects").EnumerateArray().First(o => Api.Str(o, "id") == membersLesson);
            Check.True(stub.GetProperty("locked").GetBoolean(), "Free accounts see Members lessons as locked stubs");
            JsonElement guest = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/catalog/entries/" + entry + "/lessons", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(guest.GetProperty("objects").EnumerateArray().Where(o => !o.GetProperty("locked").GetBoolean()).Select(o => Api.Str(o, "id")).SequenceEqual(new[] { publicLesson }), "Signed out, only the Public lesson opens");

            JsonElement publicTeaching = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/public/catalog/entries/" + entry + "/lessons", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(TrainingTestKit.Ids(publicTeaching).SequenceEqual(new[] { publicLesson }), "The public variant lists Public lessons only");
            Check.True(publicTeaching.GetProperty("maxResults").GetInt32() <= 50, "Public pages are bounded");
            string draft = await TrainingTestKit.DraftEntryAsync(f, "Draft taught", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/catalog/entries/" + draft + "/lessons", null, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/catalog/entries/" + draft + "/lessons", member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task LifecycleAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string kept = await TrainingTestKit.LessonAsync(f, admin, "Kept lesson", ct).ConfigureAwait(false);
            string archived = await TrainingTestKit.LessonAsync(f, admin, "Archived lesson", ct).ConfigureAwait(false);
            string deleted = await TrainingTestKit.LessonAsync(f, admin, "Deleted lesson", ct).ConfigureAwait(false);
            string entry = await TrainingTestKit.PublishedEntryAsync(f, "Lifecycle movement", ct).ConfigureAwait(false);
            string withdrawn = await TrainingTestKit.PublishedEntryAsync(f, "Withdrawn movement", ct).ConfigureAwait(false);
            string removed = await TrainingTestKit.PublishedEntryAsync(f, "Removed movement", ct).ConfigureAwait(false);
            await SetAsync(f, admin.AccessToken, kept, new[] { new { catalogEntryId = entry }, new { catalogEntryId = withdrawn }, new { catalogEntryId = removed } }, ct).ConfigureAwait(false);
            await SetAsync(f, admin.AccessToken, archived, new[] { new { catalogEntryId = entry } }, ct).ConfigureAwait(false);
            await SetAsync(f, admin.AccessToken, deleted, new[] { new { catalogEntryId = entry } }, ct).ConfigureAwait(false);

            await Api.ExpectAsync(f, HttpMethod.Post, _Admin + archived + "/archive", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement teaching = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/catalog/entries/" + entry + "/lessons", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!TrainingTestKit.Ids(teaching).Contains(archived) && TrainingTestKit.Ids(teaching).Contains(kept), "An archived lesson leaves Lessons that teach this");

            await TrainingTestKit.SetEntryStatusAsync(f, withdrawn, "Draft", ct).ConfigureAwait(false);
            JsonElement techniques = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + kept + "/catalog-entries", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(TrainingTestKit.Ids(techniques).SequenceEqual(new[] { entry, removed }), "An unpublished entry leaves Techniques in this lesson for members");
            JsonElement staff = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/lessons/" + kept + "/catalog-entries", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(staff.GetProperty("objects").EnumerateArray().Any(o => Api.Str(o, "id") == withdrawn && Api.Str(o, "warning") == "EntryNotPublished"), "Staff see a warning");

            await Api.ExpectAsync(f, HttpMethod.Delete, _Admin + deleted, admin.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.Equal(0, (await f.Database.LessonCatalogLinks.ListForLessonAsync(deleted, ct).ConfigureAwait(false)).Count, "Deleting a lesson removes its links");
            CatalogEntry removedEntry = (await f.Database.ExerciseCatalog.ReadAsync(removed, ct).ConfigureAwait(false))!;
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/catalog/entries/" + removed + "?expectedRevision=" + removedEntry.Revision, HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            List<LessonCatalogEntry> remaining = await f.Database.LessonCatalogLinks.ListForLessonAsync(kept, ct).ConfigureAwait(false);
            Check.True(remaining.Select(l => l.CatalogEntryId).SequenceEqual(new[] { entry, withdrawn }), "Deleting an entry removes its links");
        }

        private static async Task EnumerationAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await TrainingTestKit.LessonAsync(f, admin, "Paged lesson", ct).ConfigureAwait(false);
            string first = await TrainingTestKit.PublishedEntryAsync(f, "Alpha movement", ct).ConfigureAwait(false);
            string second = await TrainingTestKit.PublishedEntryAsync(f, "Beta movement", ct).ConfigureAwait(false);
            await SetAsync(f, admin.AccessToken, lesson, new object[] { new { catalogEntryId = first, labels = new[] { "warmup" } }, new { catalogEntryId = second } }, ct).ConfigureAwait(false);
            string path = "/api/v1/lessons/" + lesson + "/catalog-entries/enumerate";
            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, path, member.AccessToken, new { maxResults = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(TrainingTestKit.Ids(page).SequenceEqual(new[] { first }) && !page.GetProperty("endOfResults").GetBoolean(), "First page");
            JsonElement next = await Api.ExpectAsync(f, HttpMethod.Post, path, member.AccessToken, new { maxResults = 1, continuationToken = Api.Str(page, "continuationToken") }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(TrainingTestKit.Ids(next).SequenceEqual(new[] { second }) && next.GetProperty("endOfResults").GetBoolean(), "Continuation");
            JsonElement capped = await Api.ExpectAsync(f, HttpMethod.Post, path, member.AccessToken, new { maxResults = 100 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(capped.GetProperty("maxResults").GetInt32() <= 50, "Capped at 50");
            JsonElement labeled = await Api.ExpectAsync(f, HttpMethod.Post, path, member.AccessToken, new { labels = new[] { "warmup" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(TrainingTestKit.Ids(labeled).SequenceEqual(new[] { first }), "Label filter on links");
            JsonElement searched = await Api.ExpectAsync(f, HttpMethod.Post, path, member.AccessToken, new { search = "beta" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(TrainingTestKit.Ids(searched).SequenceEqual(new[] { second }), "Search covers entry names");
            JsonElement lessons = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/catalog/entries/" + first + "/lessons/enumerate", member.AccessToken, new { maxResults = 500 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(lessons.GetProperty("maxResults").GetInt32() <= 50, "Lessons that teach this capped at 50");
        }

        private static async Task CatalogOffAsync(HttpFixture f, CancellationToken ct)
        {
            TrainingTestKit.Relax(f);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await TrainingTestKit.LessonAsync(f, admin, "Plain lesson", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Admin + lesson + "/catalog-entries", admin.AccessToken, new { items = Array.Empty<object>() }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/lessons/" + lesson + "/catalog-entries", member.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/catalog/entries/cex_x/lessons", member.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/public/catalog/entries/cex_x/lessons", null, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
        }

        private static async Task PropertyIsolationAsync(HttpFixture f, CancellationToken ct)
        {
            TrainingTestKit.Relax(f);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            string lesson = await TrainingTestKit.LessonAsync(f, admin, "Isolated lesson", ct).ConfigureAwait(false);
            string entry = await TrainingTestKit.PublishedEntryAsync(f, "Isolated movement", ct).ConfigureAwait(false);
            await SetAsync(f, admin.AccessToken, lesson, new[] { new { catalogEntryId = entry, labels = new[] { "mine" } } }, ct).ConfigureAwait(false);
            DatabaseDriverBase other = f.Raw.ForProperty(PropertyIds.Generate());
            Check.Equal(0, (await other.LessonCatalogLinks.ListForLessonAsync(lesson, ct).ConfigureAwait(false)).Count, "Another property lists none");
            Check.Equal(0, (await other.LessonCatalogLinks.ForEntryAsync(entry, new[] { lesson }, ct).ConfigureAwait(false)).Count, "Another property maps none");
            Check.Equal(0, await other.LessonCatalogLinks.DeleteForLessonAsync(lesson, ct).ConfigureAwait(false), "Another property's lesson deletion removes nothing");
            Check.Equal(0, await other.LessonCatalogLinks.DeleteForEntryAsync(entry, ct).ConfigureAwait(false), "Another property's entry deletion removes nothing");
            Check.Equal(1, (await f.Database.LessonCatalogLinks.ListForLessonAsync(lesson, ct).ConfigureAwait(false)).Count, "The link remains");
            Check.Throws<PropertyScopeException>(() => PropertyScopeGuard.Verify("SELECT id FROM lessoncatalogentries WHERE lessonid = 'x';"), "Unscoped link SQL is refused");
        }

        #endregion
    }
}
