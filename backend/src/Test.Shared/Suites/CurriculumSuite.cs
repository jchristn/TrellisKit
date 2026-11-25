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
    /// Curricula (docs/TRELLISKIT.md, Ranks, curricula, and attendance): item satisfaction from lesson completion and
    /// series completion, self-completion versus verification, visibility, archive, item identity, validation, and the
    /// curriculum requirement of a rank.
    /// </summary>
    public static class CurriculumSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Curricula";

        #endregion

        #region Private-Members

        private const string _Admin = "/api/v1/admin/curricula";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Curricula (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "Curricula.Lifecycle", "Editors create, fill, publish (409 CurriculumEmpty when empty), and archive; members see only published curricula; archive keeps progress; members cannot edit", LifecycleAsync),
                SuiteCases.Http(Id, "Curricula.LessonAndSeries", "Lesson items complete from LessonProgress.Completed and Series items when every published lesson is complete", SatisfactionAsync),
                SuiteCases.Http(Id, "Curricula.SelfCompleteVersusVerify", "Optional CatalogEntry and Custom items count when self-completed; required ones need verification by a rank awarder; lesson items are not self-completable; verified items cannot be undone", SelfCompleteAsync),
                SuiteCases.Http(Id, "Curricula.Visibility", "Hidden curricula are not listed but open by identifier; drafts and archived curricula are 404 for members; staff see every status", VisibilityAsync),
                SuiteCases.Http(Id, "Curricula.ItemsAndValidation", "Item replacement keeps listed identifiers and their progress, deletes the rest, validates targets, types, rank groups, and text, and stores catalog entry identifiers as text", ItemsCaseAsync),
                SuiteCases.Http(Id, "Curricula.RankRequirement", "A rank requirement with a curriculum counts the required items of the next option's rank group", RequirementAsync)
            });
        }

        #endregion

        #region Private-Methods

        private static async Task<string> CreateAsync(HttpFixture f, string bearer, CancellationToken ct, object? extra = null)
        {
            Dictionary<string, object?> body = new Dictionary<string, object?> { { "name", "Foundations " + Guid.NewGuid().ToString("N").Substring(0, 6) }, { "description", "Core skills" }, { "fieldKey", "rank" } };
            if (extra != null) foreach (System.Reflection.PropertyInfo p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
            JsonElement created = await Api.ExpectAsync(f, HttpMethod.Post, _Admin, bearer, body, HttpStatusCode.Created, ct).ConfigureAwait(false);
            return Api.Str(created, "id");
        }

        private static Task<JsonElement> ItemsAsync(HttpFixture f, string bearer, string id, object[] items, CancellationToken ct, HttpStatusCode expected = HttpStatusCode.OK)
        {
            return Api.ExpectAsync(f, HttpMethod.Put, _Admin + "/" + id + "/items", bearer, new { items }, expected, ct);
        }

        private static JsonElement Item(JsonElement curriculum, int index)
        {
            return curriculum.GetProperty("items")[index];
        }

        private static async Task LifecycleAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember editor = await LibraryTestKit.EditorAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin, member.AccessToken, new { name = "Nope" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin, editor.AccessToken, new { name = "" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin, editor.AccessToken, new { name = "Bad", visibility = "Everyone" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            string id = await CreateAsync(f, editor.AccessToken, ct, new { labels = new[] { "core" } }).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin + "/" + id + "/publish", editor.AccessToken, null, HttpStatusCode.Conflict, "CurriculumEmpty", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/curricula/" + id, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement filled = await ItemsAsync(f, editor.AccessToken, id, new object[] { new { itemType = "Custom", customText = "Warm-up drill", optionKey = "Beginner", required = false } }, ct).ConfigureAwait(false);
            string item = Api.Str(Item(filled, 0), "id");
            await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/" + id + "/publish", editor.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin + "/" + id + "/publish", editor.AccessToken, null, HttpStatusCode.Conflict, "InvalidStatus", ct).ConfigureAwait(false);
            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/curricula/enumerate", member.AccessToken, new { labels = new[] { "core" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(list.GetProperty("objects").EnumerateArray().Any(c => Api.Str(c, "id") == id), "Published curriculum listed");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/curricula/" + id + "/items/" + item + "/complete", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/curricula/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(100, detail.GetProperty("progress").GetProperty("percent").GetInt32(), "Optional custom item counts once self-completed");
            await Api.ExpectAsync(f, HttpMethod.Put, _Admin + "/" + id, editor.AccessToken, new { name = "Foundations renamed" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/" + id + "/archive", editor.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/curricula/" + id, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/curricula/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!list.GetProperty("objects").EnumerateArray().Any(c => Api.Str(c, "id") == id), "Archived curriculum not listed");
            Check.Equal(1L, await RankTestKit.CountAsync(f, "SELECT COUNT(*) FROM curriculumprogress WHERE propertyid = '" + RankTestKit.Pid(f) + "' AND curriculumid = '" + id + "'", ct).ConfigureAwait(false), "Archive keeps progress");
            JsonElement staffList = await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/enumerate", editor.AccessToken, new { filters = new { status = "Archived" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(staffList.GetProperty("objects").EnumerateArray().Any(c => Api.Str(c, "id") == id && Api.Str(c, "name") == "Foundations renamed"), "Staff list filters by status");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin + "/enumerate", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            string pid = RankTestKit.Pid(f);
            foreach (string action in new[] { "curriculum.create", "curriculum.items", "curriculum.publish", "curriculum.update", "curriculum.archive" })
                Check.True(await RankTestKit.CountAsync(f, "SELECT COUNT(*) FROM auditrecords WHERE propertyid = '" + pid + "' AND action = '" + action + "' AND targetid = '" + id + "'", ct).ConfigureAwait(false) == 1, "Audited " + action);
        }

        private static async Task SatisfactionAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement series = await LibraryTestKit.SeriesAsync(f, admin.AccessToken, "Fundamentals series", ct).ConfigureAwait(false);
            string seriesId = Api.Str(series, "id");
            string first = Api.Str(await LibraryTestKit.LessonAsync(f, admin.AccessToken, seriesId, "Fundamentals one", ct).ConfigureAwait(false), "id");
            string second = Api.Str(await LibraryTestKit.LessonAsync(f, admin.AccessToken, seriesId, "Fundamentals two", ct).ConfigureAwait(false), "id");
            await LibraryTestKit.LessonAsync(f, admin.AccessToken, seriesId, "Fundamentals draft", ct, null, false).ConfigureAwait(false);
            string id = await CreateAsync(f, admin.AccessToken, ct).ConfigureAwait(false);
            await ItemsAsync(f, admin.AccessToken, id, new object[] { new { itemType = "Lesson", targetId = first, optionKey = "Beginner" }, new { itemType = "Series", targetId = seriesId, optionKey = "Intermediate" } }, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/" + id + "/publish", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/curricula/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Fundamentals one", Api.Str(Item(detail, 0), "title"), "Lesson title resolved");
            Check.Equal("Beginner", Api.Str(Item(detail, 0), "optionLabel"), "Rank group label");
            Check.Equal("Incomplete", Api.Str(Item(detail, 0), "state"), "Lesson incomplete");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/" + first + "/complete", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/curricula/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Completed", Api.Str(Item(detail, 0), "state"), "Lesson item completes from LessonProgress.Completed");
            Check.True(!Item(detail, 1).GetProperty("satisfied").GetBoolean(), "Series not complete with one of two published lessons");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/" + second + "/complete", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/curricula/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Completed", Api.Str(Item(detail, 1), "state"), "Series complete when every published lesson is (the draft does not count)");
            Check.Equal(2, detail.GetProperty("progress").GetProperty("completedRequired").GetInt32(), "Both required items done");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/curricula/" + id + "/items/" + Api.Str(Item(detail, 0), "id") + "/complete", member.AccessToken, null, HttpStatusCode.Conflict, "NotSelfCompletable", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/lessons/" + first + "/uncomplete", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/curricula/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Incomplete", Api.Str(Item(detail, 0), "state"), "Uncompleting the lesson reopens the item");
        }

        private static async Task SelfCompleteAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember awarder = await RankTestKit.AwarderAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string id = await CreateAsync(f, admin.AccessToken, ct).ConfigureAwait(false);
            JsonElement filled = await ItemsAsync(f, admin.AccessToken, id, new object[]
            {
                new { itemType = "CatalogEntry", targetId = "cat_example", required = true },
                new { itemType = "Custom", customText = "Teach a warm-up", required = false }
            }, ct).ConfigureAwait(false);
            Check.True(Item(filled, 0).GetProperty("title").ValueKind == JsonValueKind.Null, "Catalog titles are not resolved");
            string catalog = Api.Str(Item(filled, 0), "id");
            string custom = Api.Str(Item(filled, 1), "id");
            await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/" + id + "/publish", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string path = "/api/v1/me/curricula/" + id + "/items/";
            JsonElement self = await Api.ExpectAsync(f, HttpMethod.Put, path + catalog + "/complete", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("SelfComplete", Api.Str(self, "state"), "Self-complete recorded");
            Check.True(!self.GetProperty("satisfied").GetBoolean(), "A required catalog item needs verification");
            await Api.ExpectAsync(f, HttpMethod.Put, path + catalog + "/complete", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement optional = await Api.ExpectAsync(f, HttpMethod.Put, path + custom + "/complete", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(optional.GetProperty("satisfied").GetBoolean(), "An optional custom item counts when self-completed");
            string verify = _Admin + "/" + id + "/progress/" + member.Id + "/items/" + catalog + "/verify";
            await Api.ExpectErrorAsync(f, HttpMethod.Post, verify, member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin + "/" + id + "/progress/" + awarder.Id + "/items/" + catalog + "/verify", awarder.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Admin + "/" + id + "/progress/" + member.Id + "/items/cui_unknown/verify", awarder.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement verified = await Api.ExpectAsync(f, HttpMethod.Post, verify, awarder.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Verified", Api.Str(verified, "state"), "Verified");
            Check.True(verified.GetProperty("satisfied").GetBoolean(), "Verified required item counts");
            Check.Equal(awarder.Id, Api.Str(verified, "verifiedBy"), "Verifier recorded");
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, path + catalog + "/complete", member.AccessToken, null, HttpStatusCode.Conflict, "AlreadyVerified", ct).ConfigureAwait(false);
            JsonElement undone = await Api.ExpectAsync(f, HttpMethod.Delete, path + custom + "/complete", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Incomplete", Api.Str(undone, "state"), "Self-completion undone");
            JsonElement staffView = await Api.ExpectAsync(f, HttpMethod.Get, _Admin + "/" + id + "/progress/" + member.Id, awarder.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, staffView.GetProperty("progress").GetProperty("completedRequired").GetInt32(), "Staff read the member's progress");
            Check.True(await RankTestKit.CountAsync(f, "SELECT COUNT(*) FROM auditrecords WHERE propertyid = '" + RankTestKit.Pid(f) + "' AND action = 'curriculum.verify' AND targetid = '" + member.Id + "'", ct).ConfigureAwait(false) == 1, "Audited curriculum.verify");
        }

        private static async Task VisibilityAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string hidden = await CreateAsync(f, admin.AccessToken, ct, new { visibility = "Hidden" }).ConfigureAwait(false);
            string draft = await CreateAsync(f, admin.AccessToken, ct, new { visibility = "Public" }).ConfigureAwait(false);
            await ItemsAsync(f, admin.AccessToken, hidden, new object[] { new { itemType = "Custom", customText = "Hidden drill" } }, ct).ConfigureAwait(false);
            await ItemsAsync(f, admin.AccessToken, draft, new object[] { new { itemType = "Custom", customText = "Draft drill" } }, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/" + hidden + "/publish", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/curricula/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<string> ids = list.GetProperty("objects").EnumerateArray().Select(c => Api.Str(c, "id")).ToList();
            Check.True(!ids.Contains(hidden), "Hidden not listed");
            Check.True(!ids.Contains(draft), "Draft not listed");
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/curricula/" + hidden, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/curricula/" + draft, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/curricula/" + draft, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/curricula/cur_unknown", member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/enumerate", admin.AccessToken, new { maxResults = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, page.GetProperty("objects").GetArrayLength(), "Bounded staff page");
        }

        private static async Task ItemsCaseAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember awarder = await RankTestKit.AwarderAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string id = await CreateAsync(f, admin.AccessToken, ct).ConfigureAwait(false);
            string loose = await CreateAsync(f, admin.AccessToken, ct, new { fieldKey = (string?)null }).ConfigureAwait(false);
            await ItemsAsync(f, admin.AccessToken, id, new object[] { new { itemType = "Lesson", targetId = "les_unknown" } }, ct, HttpStatusCode.BadRequest).ConfigureAwait(false);
            await ItemsAsync(f, admin.AccessToken, id, new object[] { new { itemType = "Technique", targetId = "x" } }, ct, HttpStatusCode.BadRequest).ConfigureAwait(false);
            await ItemsAsync(f, admin.AccessToken, id, new object[] { new { itemType = "Custom", customText = new string('x', 301) } }, ct, HttpStatusCode.BadRequest).ConfigureAwait(false);
            await ItemsAsync(f, admin.AccessToken, id, new object[] { new { itemType = "CatalogEntry", targetId = "bad id with spaces" } }, ct, HttpStatusCode.BadRequest).ConfigureAwait(false);
            await ItemsAsync(f, admin.AccessToken, id, new object[] { new { itemType = "Custom", customText = "x", optionKey = "Expert" } }, ct, HttpStatusCode.BadRequest).ConfigureAwait(false);
            await ItemsAsync(f, admin.AccessToken, loose, new object[] { new { itemType = "Custom", customText = "x", optionKey = "Beginner" } }, ct, HttpStatusCode.BadRequest).ConfigureAwait(false);
            await ItemsAsync(f, admin.AccessToken, id, Enumerable.Range(0, 501).Select(i => (object)new { itemType = "Custom", customText = "x" }).ToArray(), ct, HttpStatusCode.BadRequest).ConfigureAwait(false);
            JsonElement filled = await ItemsAsync(f, admin.AccessToken, id, new object[]
            {
                new { itemType = "CatalogEntry", targetId = "cat:skill.part-1", optionKey = "beginner", labels = new[] { "core" } },
                new { itemType = "Custom", customText = "Removed later" }
            }, ct).ConfigureAwait(false);
            string kept = Api.Str(Item(filled, 0), "id");
            string dropped = Api.Str(Item(filled, 1), "id");
            Check.Equal("Beginner", Api.Str(Item(filled, 0), "optionKey"), "Option key normalized");
            Check.Equal("cat:skill.part-1", Api.Str(Item(filled, 0), "targetId"), "Catalog id stored as text");
            await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/" + id + "/publish", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/" + id + "/progress/" + member.Id + "/items/" + kept + "/verify", awarder.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/curricula/" + id + "/items/" + dropped + "/complete", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await ItemsAsync(f, admin.AccessToken, id, new object[] { new { id = "cui_unknown", itemType = "Custom", customText = "x" } }, ct, HttpStatusCode.BadRequest).ConfigureAwait(false);
            JsonElement replaced = await ItemsAsync(f, admin.AccessToken, id, new object[]
            {
                new { itemType = "Custom", customText = "New first" },
                new { id = kept, itemType = "CatalogEntry", targetId = "cat:skill.part-1", optionKey = "Beginner" }
            }, ct).ConfigureAwait(false);
            Check.Equal(kept, Api.Str(Item(replaced, 1), "id"), "Listed id kept");
            Check.Equal(1, Item(replaced, 1).GetProperty("position").GetInt32(), "Position follows the list");
            JsonElement detail = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/curricula/" + id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Verified", Api.Str(Item(detail, 1), "state"), "Progress kept for the listed item");
            string pid = RankTestKit.Pid(f);
            Check.Equal(0L, await RankTestKit.CountAsync(f, "SELECT COUNT(*) FROM curriculumprogress WHERE propertyid = '" + pid + "' AND itemid = '" + dropped + "'", ct).ConfigureAwait(false), "Progress of removed items deleted");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Admin + "/" + id, admin.AccessToken, new { fieldKey = (string?)null }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        private static async Task RequirementAsync(HttpFixture f, CancellationToken ct)
        {
            RankTestKit.Enable(f);
            TestMember awarder = await RankTestKit.AwarderAsync(f, ct).ConfigureAwait(false);
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, HttpFixture.AdminToken, awarder.Id, "Advanced", 3, ct).ConfigureAwait(false);
            await RankTestKit.AwardAsync(f, awarder.AccessToken, member.Id, "Beginner", 3, ct).ConfigureAwait(false);
            string id = await CreateAsync(f, HttpFixture.AdminToken, ct).ConfigureAwait(false);
            JsonElement filled = await ItemsAsync(f, HttpFixture.AdminToken, id, new object[]
            {
                new { itemType = "Custom", customText = "Intermediate requirement", optionKey = "Intermediate", required = true },
                new { itemType = "Custom", customText = "Beginner extra", optionKey = "Beginner", required = true }
            }, ct).ConfigureAwait(false);
            string requiredItem = Api.Str(Item(filled, 0), "id");
            await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/" + id + "/publish", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/ranks/requirements", HttpFixture.AdminToken, new { requirements = new object[] { new { optionKey = "Intermediate", curriculumId = "cur_unknown" } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/ranks/requirements", HttpFixture.AdminToken, new { requirements = new object[] { new { optionKey = "Intermediate", curriculumId = id } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement next = (await RankTestKit.MineAsync(f, member, ct).ConfigureAwait(false)).GetProperty("next");
            Check.Equal(1, next.GetProperty("curriculum").GetProperty("required").GetInt32(), "Only the next option's group counts");
            Check.True(!next.GetProperty("eligible").GetBoolean(), "Curriculum not met");
            await Api.ExpectAsync(f, HttpMethod.Post, _Admin + "/" + id + "/progress/" + member.Id + "/items/" + requiredItem + "/verify", awarder.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            next = (await RankTestKit.MineAsync(f, member, ct).ConfigureAwait(false)).GetProperty("next");
            Check.True(next.GetProperty("curriculumMet").GetBoolean(), "Curriculum met");
            Check.True(next.GetProperty("eligible").GetBoolean(), "Eligible with no other requirement");
        }

        #endregion
    }
}
