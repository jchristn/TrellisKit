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
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services;

    /// <summary>
    /// Generic saves and private collections (REST_API.md, Saves): idempotent save and unsave, limits, batched state,
    /// collections, member and property isolation, access loss, target deletion, export and account deletion, note
    /// review, labels and tags, and continuation tokens.
    /// </summary>
    public static class FavoriteSuite
    {
        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "Saves";

        private const string _Saves = "/api/v1/me/saves";
        private const string _Collections = "/api/v1/me/save-collections";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Descriptor.</returns>
        public static TestSuiteDescriptor Build() => new TestSuiteDescriptor(Id, "Saves and collections", new[]
        {
            SuiteCases.Http(Id, "Saves.Idempotent", "PUT creates once and updates after; DELETE is idempotent; the kill switch maps the routes", IdempotentAsync),
            SuiteCases.Http(Id, "Saves.TargetTypes", "Every engine target type saves with a summary; CatalogEntry is NotAvailable without a catalog; unknown targets are 404", TargetTypesAsync),
            SuiteCases.Http(Id, "Saves.Limits", "saves.maxPerMember and saves.maxCollectionsPerMember answer 409 LimitReached", LimitsAsync),
            SuiteCases.Http(Id, "Saves.StateBounds", "State answers 100 targets in order and rejects 101 and unknown types", StateAsync),
            SuiteCases.Http(Id, "Saves.Collections", "Duplicate names, rename, reorder, filter by collection, and delete keeps saves", CollectionsAsync),
            SuiteCases.Http(Id, "Saves.MemberIsolation", "Two members: no cross-member collection link, list, filter, or edit", MemberIsolationAsync),
            SuiteCases.Http(Id, "Saves.AccessLoss", "A target the member can no longer open shows no title and cannot be saved", AccessLossAsync),
            SuiteCases.Http(Id, "Saves.TargetDeletion", "Deleting a lesson, venue, or post removes every save of it and its memberships", TargetDeletionAsync),
            SuiteCases.Http(Id, "Saves.ExportAndDeletion", "Account export carries saves and collections; account deletion removes them", ExportAndDeletionAsync),
            SuiteCases.Http(Id, "Saves.NoteReview", "Notes write content changelog rows, pass the word filter, and are bounded", NoteReviewAsync),
            SuiteCases.Http(Id, "Saves.MetadataAndPaging", "Label, tag, and target type filters; continuation tokens; invalid tokens", MetadataAndPagingAsync),
            SuiteCases.Http(Id, "Saves.FeatureOff", "features.saves false answers 404 NotAvailable", p => p.Features.Saves = false, FeatureOffAsync),
            SuiteCases.Http(Id, "Saves.PropertyIsolation", "Another property sees, links, and deletes none of this property's saves", PropertyIsolationAsync)
        });

        private static string SavePath(string type, string id) => _Saves + "/" + type + "/" + id;

        private static async Task<JsonElement> SaveAsync(HttpFixture f, TestMember member, string type, string id, CancellationToken ct, object? body = null)
        {
            return await Api.ExpectAsync(f, HttpMethod.Put, SavePath(type, id), member.AccessToken, body ?? new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task<string> LessonAsync(HttpFixture f, string title, CancellationToken ct)
        {
            LibraryTestKit.Relax(f);
            JsonElement series = await LibraryTestKit.SeriesAsync(f, HttpFixture.AdminToken, title + " series", ct).ConfigureAwait(false);
            JsonElement lesson = await LibraryTestKit.LessonAsync(f, HttpFixture.AdminToken, Api.Str(series, "id"), title, ct).ConfigureAwait(false);
            return Api.Str(lesson, "id");
        }

        private static async Task<string> CollectionAsync(HttpFixture f, TestMember member, string name, CancellationToken ct)
        {
            JsonElement created = await Api.ExpectAsync(f, HttpMethod.Post, _Collections, member.AccessToken, new { name }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            return Api.Str(created, "id");
        }

        private static async Task<JsonElement> ListAsync(HttpFixture f, TestMember member, object body, CancellationToken ct)
        {
            return await Api.ExpectAsync(f, HttpMethod.Post, _Saves + "/enumerate", member.AccessToken, body, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static List<string> Ids(JsonElement page)
        {
            return page.GetProperty("objects").EnumerateArray().Select(o => Api.Str(o, "id")).ToList();
        }

        private static Task<long> CountAsync(HttpFixture f, string sql, CancellationToken ct)
        {
            return ContentTestKit.ScalarAsync(f, sql, ct);
        }

        private static async Task IdempotentAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await LessonAsync(f, "Idempotent lesson", ct).ConfigureAwait(false);
            JsonElement first = await SaveAsync(f, member, "Lesson", lesson, ct).ConfigureAwait(false);
            JsonElement second = await SaveAsync(f, member, "lesson", lesson, ct, new { note = "Practice this" }).ConfigureAwait(false);
            Check.Equal(Api.Str(first, "id"), Api.Str(second, "id"), "Second PUT updates the same save");
            Check.True(Api.Str(first, "id").StartsWith("fav_", StringComparison.Ordinal), "fav_ prefix");
            Check.Equal("Practice this", Api.Str(second, "note"), "Note stored");
            Check.True(second.GetProperty("target").GetProperty("available").GetBoolean(), "Target available");
            Check.Equal("Idempotent lesson", Api.Str(second.GetProperty("target"), "title"), "Target title");
            Check.True(second.TryGetProperty("labels", out JsonElement _) && second.TryGetProperty("tags", out JsonElement _), "Labels and tags always present");
            JsonElement third = await SaveAsync(f, member, "Lesson", lesson, ct, new { labels = new[] { "later" } }).ConfigureAwait(false);
            Check.Equal("Practice this", Api.Str(third, "note"), "An omitted note keeps the stored note");
            Check.Equal(1L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM favorites WHERE userid = '" + member.Id + "';", ct).ConfigureAwait(false), "One row");
            await Api.ExpectAsync(f, HttpMethod.Delete, SavePath("Lesson", lesson), member.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, SavePath("Lesson", lesson), member.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.Equal(0L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM favorites WHERE userid = '" + member.Id + "';", ct).ConfigureAwait(false), "Deleted");
            Check.Equal(0L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM labels WHERE targettype = 'Favorite' AND targetid = '" + Api.Str(first, "id") + "';", ct).ConfigureAwait(false), "Labels deleted with the save");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, SavePath("Nope", lesson), member.AccessToken, new { }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, SavePath("Lesson", lesson), null, new { }, HttpStatusCode.Unauthorized, "Unauthorized", ct).ConfigureAwait(false);
            Check.True(KillSwitchCatalog.FeaturesFor("PUT", SavePath("Lesson", lesson)).Contains("saves"), "Kill switch covers saves");
            Check.True(KillSwitchCatalog.FeaturesFor("GET", _Collections).Contains("saves"), "Kill switch covers collections");
            Check.True(KillSwitchCatalog.IsFeature("saves"), "saves is an engine feature key");
            TestMember free = await Api.RegisterAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, SavePath("Lesson", lesson), free.AccessToken, new { }, (HttpStatusCode)402, "MembershipRequired", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, _Saves, free.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
        }

        private static async Task TargetTypesAsync(HttpFixture f, CancellationToken ct)
        {
            ContentTestKit.Relax(f);
            ForumTestKit.Relax(f);
            TestMember member = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            string lesson = await LessonAsync(f, "Typed lesson", ct).ConfigureAwait(false);
            JsonElement series = await LibraryTestKit.SeriesAsync(f, HttpFixture.AdminToken, "Typed series", ct).ConfigureAwait(false);
            string venue = await DirectoryTestKit.VenueIdAsync(f, "Typed venue", ct).ConfigureAwait(false);
            string eventId = await EventTestKit.PublishedAsync(f, HttpFixture.AdminToken, "Typed event", ct).ConfigureAwait(false);
            string post = await ContentTestKit.PostIdAsync(f, member.AccessToken, "A post worth keeping", ct).ConfigureAwait(false);
            await ContentTestKit.PublishAsync(f, ct).ConfigureAwait(false);
            string forum = await ForumTestKit.DefaultForumIdAsync(f, member.AccessToken, ct).ConfigureAwait(false);
            string thread = await ForumTestKit.ThreadIdAsync(f, member.AccessToken, forum, "Typed thread", ct).ConfigureAwait(false);
            await ContentTestKit.PublishAsync(f, ct).ConfigureAwait(false);

            Dictionary<string, string> targets = new Dictionary<string, string>
            {
                { "Lesson", lesson }, { "Series", Api.Str(series, "id") }, { "Venue", venue }, { "Event", eventId }, { "Post", post }, { "Thread", thread }
            };
            foreach (KeyValuePair<string, string> target in targets)
            {
                JsonElement saved = await SaveAsync(f, member, target.Key, target.Value, ct).ConfigureAwait(false);
                Check.Equal(target.Key, Api.Str(saved, "targetType"), target.Key + " type");
                Check.True(saved.GetProperty("target").GetProperty("available").GetBoolean(), target.Key + " available");
                Check.True(!String.IsNullOrEmpty(Api.Str(saved.GetProperty("target"), "href")), target.Key + " link");
            }
            await Api.ExpectErrorAsync(f, HttpMethod.Put, SavePath("CatalogEntry", "cat_missing"), member.AccessToken, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, SavePath("Lesson", "les_missing"), member.AccessToken, new { }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, SavePath("Thread", "thr_missing"), member.AccessToken, new { }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);

            await f.Database.Favorites.UpsertAsync(new Favorite { Id = IdGenerator.Generate("fav_"), UserId = member.Id, TargetType = FavoriteTargetTypeEnum.CatalogEntry, TargetId = "cat_stored" }, false, ct).ConfigureAwait(false);
            JsonElement catalog = await ListAsync(f, member, new { filters = new { targetTypes = new[] { "CatalogEntry" } } }, ct).ConfigureAwait(false);
            Check.Equal(1, catalog.GetProperty("objects").GetArrayLength(), "Stored catalog save listed");
            JsonElement entry = catalog.GetProperty("objects")[0].GetProperty("target");
            Check.True(!entry.GetProperty("available").GetBoolean() && entry.GetProperty("title").ValueKind == JsonValueKind.Null, "Catalog entries resolve as unavailable without a catalog provider");

            JsonElement all = await ListAsync(f, member, new { maxResults = 100 }, ct).ConfigureAwait(false);
            Check.Equal(7, all.GetProperty("objects").GetArrayLength(), "Every save listed");
            JsonElement library = await ListAsync(f, member, new { filters = new { targetTypes = "Lesson,Series" } }, ct).ConfigureAwait(false);
            Check.Equal(2, library.GetProperty("objects").GetArrayLength(), "Target type filter (comma list)");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Saves + "/enumerate", member.AccessToken, new { filters = new { targetTypes = new[] { "Nope" } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement get = await Api.ExpectAsync(f, HttpMethod.Get, _Saves + "?targetTypes=Venue", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, get.GetProperty("objects").GetArrayLength(), "GET form with a filter");

            f.Property.Features.Forums = false;
            JsonElement offList = await ListAsync(f, member, new { filters = new { targetTypes = new[] { "Thread" } } }, ct).ConfigureAwait(false);
            Check.True(!offList.GetProperty("objects")[0].GetProperty("target").GetProperty("available").GetBoolean(), "A target whose feature is off is unavailable");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, SavePath("Thread", thread), member.AccessToken, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            f.Property.Features.Forums = true;
        }

        private static async Task LimitsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await LessonAsync(f, "Limited lesson", ct).ConfigureAwait(false);
            string other = await LessonAsync(f, "Over the limit", ct).ConfigureAwait(false);
            f.Settings.Saves.MaxPerMember = 1;
            Check.Equal(100, f.Settings.Saves.MaxPerMember, "maxPerMember clamps to its minimum");
            await SaveAsync(f, member, "Lesson", lesson, ct).ConfigureAwait(false);
            for (int i = 1; i < 100; i++)
                await f.Database.Favorites.UpsertAsync(new Favorite { Id = IdGenerator.Generate("fav_"), UserId = member.Id, TargetType = FavoriteTargetTypeEnum.Post, TargetId = "pst_filler" + i }, false, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, SavePath("Lesson", other), member.AccessToken, new { }, HttpStatusCode.Conflict, "LimitReached", ct).ConfigureAwait(false);
            await SaveAsync(f, member, "Lesson", lesson, ct, new { note = "Existing saves still update" }).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, SavePath("Lesson", lesson), member.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await SaveAsync(f, member, "Lesson", other, ct).ConfigureAwait(false);

            f.Settings.Saves.MaxCollectionsPerMember = 1;
            await CollectionAsync(f, member, "Only one", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Collections, member.AccessToken, new { name = "Second" }, HttpStatusCode.Conflict, "LimitReached", ct).ConfigureAwait(false);
            f.Settings.Saves.MaxCollectionsPerMember = 50;
            f.Settings.Saves.MaxPerMember = 2000;
            await Api.ExpectErrorAsync(f, HttpMethod.Put, SavePath("Lesson", other), member.AccessToken, new { collectionIds = Enumerable.Range(0, 21).Select(i => "fvc_" + i).ToArray() }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        private static async Task StateAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await LessonAsync(f, "State lesson", ct).ConfigureAwait(false);
            JsonElement saved = await SaveAsync(f, member, "Lesson", lesson, ct).ConfigureAwait(false);
            List<object> targets = new List<object> { new { targetType = "Lesson", targetId = lesson } };
            for (int i = 1; i < 100; i++) targets.Add(new { targetType = "Post", targetId = "pst_state" + i });
            JsonElement state = await Api.ExpectAsync(f, HttpMethod.Post, _Saves + "/state", member.AccessToken, new { targets }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement items = state.GetProperty("items");
            Check.Equal(100, items.GetArrayLength(), "100 targets answered");
            Check.True(items[0].GetProperty("saved").GetBoolean() && Api.Str(items[0], "favoriteId") == Api.Str(saved, "id"), "Saved target first, in request order");
            Check.True(!items[1].GetProperty("saved").GetBoolean() && items[1].GetProperty("favoriteId").ValueKind == JsonValueKind.Null, "Unsaved target");
            targets.Add(new { targetType = "Post", targetId = "pst_state_extra" });
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Saves + "/state", member.AccessToken, new { targets }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Saves + "/state", member.AccessToken, new { targets = new object[0] }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Saves + "/state", member.AccessToken, new { targets = new[] { new { targetType = "Nope", targetId = "x" } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        private static async Task CollectionsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await LessonAsync(f, "Collected lesson", ct).ConfigureAwait(false);
            string loose = await LessonAsync(f, "Loose lesson", ct).ConfigureAwait(false);
            string guard = await CollectionAsync(f, member, "Guard work", ct).ConfigureAwait(false);
            string drills = await CollectionAsync(f, member, "Drills", ct).ConfigureAwait(false);
            Check.True(guard.StartsWith("fvc_", StringComparison.Ordinal), "fvc_ prefix");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Collections, member.AccessToken, new { name = "guard WORK" }, HttpStatusCode.Conflict, "DuplicateName", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Collections, member.AccessToken, new { name = " " }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Collections, member.AccessToken, new { name = new string('x', 61) }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Collections + "/" + drills, member.AccessToken, new { name = "Guard Work" }, HttpStatusCode.Conflict, "DuplicateName", ct).ConfigureAwait(false);
            JsonElement renamed = await Api.ExpectAsync(f, HttpMethod.Put, _Collections + "/" + drills, member.AccessToken, new { name = "Solo drills", labels = new[] { "solo" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Solo drills", Api.Str(renamed, "name"), "Renamed");
            Check.Equal("solo", renamed.GetProperty("labels")[0].GetString(), "Collection labels");

            JsonElement saved = await SaveAsync(f, member, "Lesson", lesson, ct, new { collectionIds = new[] { guard, drills } }).ConfigureAwait(false);
            Check.Equal(2, saved.GetProperty("collectionIds").GetArrayLength(), "In two collections");
            await SaveAsync(f, member, "Lesson", loose, ct).ConfigureAwait(false);
            JsonElement inGuard = await ListAsync(f, member, new { filters = new { collectionId = guard } }, ct).ConfigureAwait(false);
            Check.True(Ids(inGuard).SequenceEqual(new[] { Api.Str(saved, "id") }), "Collection filter");
            JsonElement collections = await Api.ExpectAsync(f, HttpMethod.Get, _Collections, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(Ids(collections).SequenceEqual(new[] { guard, drills }), "Position order");
            Check.Equal(1, collections.GetProperty("objects")[0].GetProperty("itemCount").GetInt32(), "Item count");

            await Api.ExpectAsync(f, HttpMethod.Put, _Collections + "/order", member.AccessToken, new { ids = new[] { drills, guard } }, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            JsonElement reordered = await Api.ExpectAsync(f, HttpMethod.Post, _Collections + "/enumerate", member.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(Ids(reordered).SequenceEqual(new[] { drills, guard }), "Reordered");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Collections + "/order", member.AccessToken, new { ids = new[] { drills } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Collections + "/order", member.AccessToken, new { ids = new[] { drills, drills } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);

            JsonElement moved = await SaveAsync(f, member, "Lesson", lesson, ct, new { collectionIds = new[] { guard } }).ConfigureAwait(false);
            Check.True(moved.GetProperty("collectionIds").EnumerateArray().Select(e => e.GetString()).SequenceEqual(new[] { guard }), "collectionIds replaces the set");
            await Api.ExpectAsync(f, HttpMethod.Delete, _Collections + "/" + guard, member.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, _Collections + "/" + guard, member.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement remaining = await ListAsync(f, member, new { }, ct).ConfigureAwait(false);
            Check.Equal(2, remaining.GetProperty("objects").GetArrayLength(), "Deleting a collection keeps its saves");
            Check.Equal(0, remaining.GetProperty("objects").EnumerateArray().Sum(o => o.GetProperty("collectionIds").GetArrayLength()), "Memberships removed");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Saves + "/enumerate", member.AccessToken, new { filters = new { collectionId = guard } }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
        }

        private static async Task MemberIsolationAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember alice = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember bob = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await LessonAsync(f, "Shared lesson", ct).ConfigureAwait(false);
            string aliceCollection = await CollectionAsync(f, alice, "Mine", ct).ConfigureAwait(false);
            string bobCollection = await CollectionAsync(f, bob, "Mine", ct).ConfigureAwait(false);
            JsonElement aliceSave = await SaveAsync(f, alice, "Lesson", lesson, ct, new { collectionIds = new[] { aliceCollection } }).ConfigureAwait(false);
            JsonElement bobSave = await SaveAsync(f, bob, "Lesson", lesson, ct).ConfigureAwait(false);
            Check.True(Api.Str(aliceSave, "id") != Api.Str(bobSave, "id"), "Each member has their own save");

            await Api.ExpectErrorAsync(f, HttpMethod.Put, SavePath("Lesson", lesson), bob.AccessToken, new { collectionIds = new[] { aliceCollection } }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            Check.True(!await f.Database.Favorites.SetCollectionsAsync(bob.Id, Api.Str(bobSave, "id"), new[] { aliceCollection }, ct).ConfigureAwait(false), "Database refuses another member's collection");
            Check.True(!await f.Database.Favorites.SetCollectionsAsync(alice.Id, Api.Str(aliceSave, "id"), new[] { bobCollection }, ct).ConfigureAwait(false), "Database refuses a foreign collection for the owner's save");
            Check.True(!await f.Database.Favorites.SetCollectionsAsync(bob.Id, Api.Str(aliceSave, "id"), new[] { bobCollection }, ct).ConfigureAwait(false), "Database refuses another member's save");
            Check.True(!await f.Database.Favorites.SetCollectionsAsync(bob.Id, Api.Str(aliceSave, "id"), Array.Empty<string>(), ct).ConfigureAwait(false), "Database never clears another member's memberships");
            Check.Equal(0L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM favoritecollectionitems i JOIN favorites s ON s.id = i.favoriteid JOIN favoritecollections c ON c.id = i.collectionid WHERE s.userid <> c.userid;", ct).ConfigureAwait(false), "No cross-member link exists");
            Check.Equal(1L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM favoritecollectionitems WHERE collectionid = '" + aliceCollection + "';", ct).ConfigureAwait(false), "The owner's link survived");

            JsonElement bobList = await ListAsync(f, bob, new { }, ct).ConfigureAwait(false);
            Check.True(Ids(bobList).SequenceEqual(new[] { Api.Str(bobSave, "id") }), "Lists only the caller's saves");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Saves + "/enumerate", bob.AccessToken, new { filters = new { collectionId = aliceCollection } }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Collections + "/" + aliceCollection, bob.AccessToken, new { name = "Taken" }, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, _Collections + "/" + aliceCollection, bob.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, _Collections + "/order", bob.AccessToken, new { ids = new[] { aliceCollection } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement bobCollections = await Api.ExpectAsync(f, HttpMethod.Get, _Collections, bob.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(Ids(bobCollections).SequenceEqual(new[] { bobCollection }), "Lists only the caller's collections");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/metadata/Favorite/" + Api.Str(aliceSave, "id"), bob.AccessToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/metadata/Favorite/" + Api.Str(aliceSave, "id"), alice.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, SavePath("Lesson", lesson), bob.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.Equal(1L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM favorites WHERE userid = '" + alice.Id + "';", ct).ConfigureAwait(false), "Another member's unsave leaves the owner's save");
        }

        private static async Task AccessLossAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await LessonAsync(f, "Soon archived", ct).ConfigureAwait(false);
            await SaveAsync(f, member, "Lesson", lesson, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/" + lesson + "/archive", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement list = await ListAsync(f, member, new { }, ct).ConfigureAwait(false);
            JsonElement target = list.GetProperty("objects")[0].GetProperty("target");
            Check.True(!target.GetProperty("available").GetBoolean(), "Archived target unavailable");
            Check.True(target.GetProperty("title").ValueKind == JsonValueKind.Null && target.GetProperty("href").ValueKind == JsonValueKind.Null, "No title or link leaks");
            Check.True(!list.GetRawText().Contains("Soon archived", StringComparison.Ordinal), "The title appears nowhere in the list");
            string other = await LessonAsync(f, "Archived first", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/" + other + "/archive", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, SavePath("Lesson", other), member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/lessons/" + lesson + "/unarchive", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement back = await ListAsync(f, member, new { }, ct).ConfigureAwait(false);
            Check.True(back.GetProperty("objects")[0].GetProperty("target").GetProperty("available").GetBoolean(), "Restored access shows the target again");
        }

        private static async Task TargetDeletionAsync(HttpFixture f, CancellationToken ct)
        {
            ContentTestKit.Relax(f);
            TestMember alice = await ContentTestKit.PosterAsync(f, ct).ConfigureAwait(false);
            TestMember bob = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await LessonAsync(f, "Doomed lesson", ct).ConfigureAwait(false);
            string collection = await CollectionAsync(f, alice, "Doomed", ct).ConfigureAwait(false);
            await SaveAsync(f, alice, "Lesson", lesson, ct, new { collectionIds = new[] { collection }, note = "Gone soon" }).ConfigureAwait(false);
            await SaveAsync(f, bob, "Lesson", lesson, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/admin/lessons/" + lesson, HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.Equal(0L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM favorites WHERE targetid = '" + lesson + "';", ct).ConfigureAwait(false), "Every member's save of the lesson removed");
            Check.Equal(0L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM favoritecollectionitems WHERE collectionid = '" + collection + "';", ct).ConfigureAwait(false), "Memberships removed");
            Check.Equal(1L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM favoritecollections WHERE id = '" + collection + "';", ct).ConfigureAwait(false), "The collection stays");

            string venue = await DirectoryTestKit.VenueIdAsync(f, "Doomed venue", ct).ConfigureAwait(false);
            await SaveAsync(f, alice, "Venue", venue, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, DirectoryTestKit.AdminVenues + "/" + venue, HttpFixture.AdminToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.Equal(0L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM favorites WHERE targetid = '" + venue + "';", ct).ConfigureAwait(false), "Venue saves removed");

            string post = await ContentTestKit.PostIdAsync(f, alice.AccessToken, "Short-lived post", ct).ConfigureAwait(false);
            await ContentTestKit.PublishAsync(f, ct).ConfigureAwait(false);
            await SaveAsync(f, bob, "Post", post, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/feed/" + post, alice.AccessToken, null, HttpStatusCode.NoContent, ct).ConfigureAwait(false);
            Check.Equal(0L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM favorites WHERE targetid = '" + post + "';", ct).ConfigureAwait(false), "Post saves removed");
            Check.Equal(0, await f.Services.Favorites.TargetsDeletedAsync(FavoriteTargetTypeEnum.Post, Array.Empty<string>(), ct).ConfigureAwait(false), "Empty deletion is a no-op");
        }

        private static async Task ExportAndDeletionAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await LessonAsync(f, "Exported lesson", ct).ConfigureAwait(false);
            string collection = await CollectionAsync(f, member, "Exported", ct).ConfigureAwait(false);
            await SaveAsync(f, member, "Lesson", lesson, ct, new { collectionIds = new[] { collection }, note = "Export me", labels = new[] { "export" } }).ConfigureAwait(false);
            await SaveAsync(f, other, "Lesson", lesson, ct).ConfigureAwait(false);
            JsonElement file = await AccountExportSuite.ExportAsync(f, member, ct).ConfigureAwait(false);
            JsonElement saves = file.GetProperty("saves");
            Check.Equal(1, saves.GetArrayLength(), "Saves exported");
            Check.Equal("Export me", Api.Str(saves[0], "note"), "Notes exported");
            Check.Equal(collection, saves[0].GetProperty("collectionIds")[0].GetString(), "Memberships exported");
            Check.Equal("export", saves[0].GetProperty("labels")[0].GetString(), "Labels exported");
            Check.Equal("Exported", Api.Str(file.GetProperty("saveCollections")[0], "name"), "Collections exported");

            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", member.AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
            Check.Equal(0L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM favorites WHERE userid = '" + member.Id + "';", ct).ConfigureAwait(false), "Saves deleted with the account");
            Check.Equal(0L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM favoritecollections WHERE userid = '" + member.Id + "';", ct).ConfigureAwait(false), "Collections deleted with the account");
            Check.Equal(0L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM labels WHERE targettype IN ('Favorite', 'FavoriteCollection') AND targetid NOT IN (SELECT id FROM favorites UNION SELECT id FROM favoritecollections);", ct).ConfigureAwait(false), "No orphan metadata");
            Check.Equal(1L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM favorites WHERE userid = '" + other.Id + "';", ct).ConfigureAwait(false), "Other members keep their saves");
            Check.True(TrellisKit.Core.Database.PropertyTables.Owned.ToList().IndexOf("favorites") < TrellisKit.Core.Database.PropertyTables.Owned.ToList().IndexOf("users"), "Purged before users");
        }

        private static async Task NoteReviewAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            string lesson = await LessonAsync(f, "Noted lesson", ct).ConfigureAwait(false);
            JsonElement saved = await SaveAsync(f, member, "Lesson", lesson, ct, new { note = "Remember the grip" }).ConfigureAwait(false);
            string id = Api.Str(saved, "id");
            Check.Equal(1L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM contentchanges WHERE targettype = 'Favorite' AND targetid = '" + id + "' AND action = 'Create' AND authoruserid = '" + member.Id + "' AND contexttype = 'Lesson';", ct).ConfigureAwait(false), "Note create recorded");
            await SaveAsync(f, member, "Lesson", lesson, ct, new { labels = new[] { "no-note-change" } }).ConfigureAwait(false);
            Check.Equal(1L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM contentchanges WHERE targettype = 'Favorite' AND targetid = '" + id + "';", ct).ConfigureAwait(false), "Unchanged note records nothing");
            await SaveAsync(f, member, "Lesson", lesson, ct, new { note = "Remember the grip and the angle" }).ConfigureAwait(false);
            await SaveAsync(f, member, "Lesson", lesson, ct, new { note = (string?)null }).ConfigureAwait(false);
            Check.Equal(1L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM contentchanges WHERE targettype = 'Favorite' AND targetid = '" + id + "' AND action = 'Update';", ct).ConfigureAwait(false), "Note edit recorded");
            Check.Equal(1L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM contentchanges WHERE targettype = 'Favorite' AND targetid = '" + id + "' AND action = 'Delete';", ct).ConfigureAwait(false), "Cleared note recorded as a deletion");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, SavePath("Lesson", lesson), member.AccessToken, new { note = new string('n', 501) }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/moderation/word-filter", admin.AccessToken, new { term = "Forbiddenword", matchType = "Word", action = "Block" }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            f.Services.WordFilter.Invalidate();
            await Api.ExpectErrorAsync(f, HttpMethod.Put, SavePath("Lesson", lesson), member.AccessToken, new { note = "a forbiddenword here" }, HttpStatusCode.BadRequest, "ContentFiltered", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Collections, member.AccessToken, new { name = "Forbiddenword list" }, HttpStatusCode.BadRequest, "ContentFiltered", ct).ConfigureAwait(false);
            JsonElement kept = await ListAsync(f, member, new { }, ct).ConfigureAwait(false);
            Check.True(kept.GetProperty("objects")[0].GetProperty("note").ValueKind == JsonValueKind.Null, "A blocked note changes nothing");
        }

        private static async Task MetadataAndPagingAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            List<string> lessons = new List<string>();
            for (int i = 0; i < 3; i++) lessons.Add(await LessonAsync(f, "Paged lesson " + i, ct).ConfigureAwait(false));
            await SaveAsync(f, member, "Lesson", lessons[0], ct, new { labels = new[] { "guard" }, tags = new Dictionary<string, string> { { "focus", "passing" } } }).ConfigureAwait(false);
            await SaveAsync(f, member, "Lesson", lessons[1], ct, new { labels = new[] { "guard" } }).ConfigureAwait(false);
            await SaveAsync(f, member, "Lesson", lessons[2], ct, new { note = "searchable phrase" }).ConfigureAwait(false);
            Check.Equal(2, (await ListAsync(f, member, new { labels = new[] { "GUARD" } }, ct).ConfigureAwait(false)).GetProperty("objects").GetArrayLength(), "Label filter");
            Check.Equal(1, (await ListAsync(f, member, new { tags = new Dictionary<string, string> { { "focus", "passing" } } }, ct).ConfigureAwait(false)).GetProperty("objects").GetArrayLength(), "Tag filter");
            Check.Equal(1, (await ListAsync(f, member, new { search = "searchable" }, ct).ConfigureAwait(false)).GetProperty("objects").GetArrayLength(), "Note search");
            Check.Equal(0, (await ListAsync(f, member, new { createdAfterUtc = DateTime.UtcNow.AddDays(1) }, ct).ConfigureAwait(false)).GetProperty("objects").GetArrayLength(), "Created window");
            JsonElement first = await ListAsync(f, member, new { maxResults = 2 }, ct).ConfigureAwait(false);
            Check.Equal(2, first.GetProperty("objects").GetArrayLength(), "Page bounded");
            Check.True(!first.GetProperty("endOfResults").GetBoolean(), "More remain");
            JsonElement second = await ListAsync(f, member, new { maxResults = 2, continuationToken = Api.Str(first, "continuationToken") }, ct).ConfigureAwait(false);
            Check.Equal(1, second.GetProperty("objects").GetArrayLength(), "Second page");
            Check.True(!Ids(first).Intersect(Ids(second)).Any(), "No repeated save");
            JsonElement capped = await ListAsync(f, member, new { maxResults = 5000 }, ct).ConfigureAwait(false);
            Check.True(capped.GetProperty("maxResults").GetInt32() <= 100, "Member maximum 100");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Saves + "/enumerate", member.AccessToken, new { continuationToken = "not-a-token" }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
            JsonElement labels = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/labels/enumerate", member.AccessToken, new { targetType = "Favorite" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(labels.GetProperty("objects").EnumerateArray().Any(o => Api.Str(o, "label") == "guard"), "Owners discover their save labels");
            TestMember stranger = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            JsonElement hidden = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/labels/enumerate", stranger.AccessToken, new { targetType = "Favorite" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(0, hidden.GetProperty("objects").GetArrayLength(), "Other members never discover them");
        }

        private static async Task FeatureOffAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, _Saves, member.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, SavePath("Lesson", "les_any"), member.AccessToken, new { }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Delete, SavePath("Lesson", "les_any"), member.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Saves + "/state", member.AccessToken, new { targets = new[] { new { targetType = "Lesson", targetId = "les_any" } } }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, _Collections, member.AccessToken, new { name = "Off" }, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
            JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!property.GetProperty("features").GetProperty("saves").GetBoolean(), "features.saves served false");
        }

        private static async Task PropertyIsolationAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            string lesson = await LessonAsync(f, "Isolated lesson", ct).ConfigureAwait(false);
            string collection = await CollectionAsync(f, member, "Isolated", ct).ConfigureAwait(false);
            JsonElement saved = await SaveAsync(f, member, "Lesson", lesson, ct, new { collectionIds = new[] { collection } }).ConfigureAwait(false);
            string favoriteId = Api.Str(saved, "id");
            DatabaseDriverBase other = f.Raw.ForProperty(PropertyIds.Generate());
            Check.True(await other.Favorites.ReadAsync(favoriteId, ct).ConfigureAwait(false) == null, "Another property cannot read the save");
            Check.Equal(0L, await other.Favorites.CountForUserAsync(member.Id, ct).ConfigureAwait(false), "Another property counts nothing");
            Check.Equal(0, (await other.Favorites.StateAsync(member.Id, new List<FavoriteTargetKey> { new FavoriteTargetKey { TargetType = FavoriteTargetTypeEnum.Lesson, TargetId = lesson } }, ct).ConfigureAwait(false)).Count, "Another property sees no state");
            Check.True(await other.Favorites.ReadCollectionAsync(member.Id, collection, ct).ConfigureAwait(false) == null, "Another property cannot read the collection");
            Check.True(!await other.Favorites.SetCollectionsAsync(member.Id, favoriteId, new[] { collection }, ct).ConfigureAwait(false), "Another property cannot link");
            Check.True(!await other.Favorites.SetCollectionsAsync(member.Id, favoriteId, Array.Empty<string>(), ct).ConfigureAwait(false), "Another property cannot unlink");
            Check.Equal(0, await other.Favorites.DeleteForTargetAsync(FavoriteTargetTypeEnum.Lesson, new[] { lesson }, ct).ConfigureAwait(false), "Another property's target deletion removes nothing");
            Check.Equal(0, await other.Favorites.DeleteForUserAsync(member.Id, ct).ConfigureAwait(false), "Another property's account deletion removes nothing");
            Check.True(!await other.Favorites.DeleteCollectionAsync(member.Id, collection, ct).ConfigureAwait(false), "Another property cannot delete the collection");
            Check.True(await f.Database.Favorites.ReadAsync(favoriteId, ct).ConfigureAwait(false) != null, "The save remains");
            Check.Equal(1L, await CountAsync(f, "SELECT COUNT(*) AS cnt FROM favoritecollectionitems WHERE favoriteid = '" + favoriteId + "';", ct).ConfigureAwait(false), "The membership remains");
            Check.Throws<PropertyScopeException>(() => PropertyScopeGuard.Verify("SELECT id FROM favorites WHERE userid = 'x';"), "Unscoped favorites SQL is refused");
            Check.Throws<PropertyScopeException>(() => PropertyScopeGuard.Verify("DELETE FROM favoritecollectionitems WHERE collectionid = 'x';"), "Unscoped membership SQL is refused");
        }
    }
}
