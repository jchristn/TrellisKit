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
    using TrellisKit.Core.Models;

    /// <summary>
    /// Shared helpers of the Training hub, lesson technique, and library search suites.
    /// </summary>
    internal static class TrainingTestKit
    {
        internal const string Overview = "/api/v1/me/training";
        internal const string Search = "/api/v1/library/search";

        internal static void Relax(HttpFixture f)
        {
            LibraryTestKit.Relax(f);
            EventTestKit.Relax(f);
            f.Settings.Search.PerMinute = 10000;
        }

        internal static async Task<string> LessonAsync(HttpFixture f, TestMember admin, string title, CancellationToken ct, object? extra = null)
        {
            JsonElement series = await LibraryTestKit.SeriesAsync(f, admin.AccessToken, title + " course", ct, extra).ConfigureAwait(false);
            JsonElement lesson = await LibraryTestKit.LessonAsync(f, admin.AccessToken, Api.Str(series, "id"), title, ct, extra).ConfigureAwait(false);
            return Api.Str(lesson, "id");
        }

        internal static async Task<string> DraftEntryAsync(HttpFixture f, string name, CancellationToken ct)
        {
            string slug = "entry-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            JsonElement created = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/catalog/entries", HttpFixture.AdminToken, new
            {
                name,
                slug,
                kind = "Technique",
                difficulty = "Beginner",
                summary = name + " summary",
                instructions = new[] { "Start in the setup position.", "Complete the movement with control." },
                sources = new[] { new { title = "Original guidance", license = "Original", method = "Manual" } }
            }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            return Api.Str(created, "id");
        }

        internal static async Task<string> PublishedEntryAsync(HttpFixture f, string name, CancellationToken ct)
        {
            string id = await DraftEntryAsync(f, name, ct).ConfigureAwait(false);
            await SetEntryStatusAsync(f, id, "Published", ct).ConfigureAwait(false);
            return id;
        }

        internal static async Task SetEntryStatusAsync(HttpFixture f, string id, string status, CancellationToken ct)
        {
            CatalogEntry entry = (await f.Database.ExerciseCatalog.ReadAsync(id, ct).ConfigureAwait(false))!;
            long revision = entry.Revision;
            entry.Status = status;
            if (status == "Published") entry.PublishedUtc = DateTime.UtcNow;
            entry.Revision++;
            Check.True(await f.Database.ExerciseCatalog.SaveAsync(entry, revision, false, null, ct).ConfigureAwait(false), "Catalog entry " + status);
        }

        internal static JsonElement Section(JsonElement overview, string key)
        {
            return overview.GetProperty("sections").EnumerateArray().First(s => Api.Str(s, "key") == key);
        }

        internal static bool Available(JsonElement overview, string key)
        {
            return Section(overview, key).GetProperty("available").GetBoolean();
        }

        internal static List<string> Segments(JsonElement overview)
        {
            return Section(overview, "Library").GetProperty("segments").EnumerateArray().Select(s => s.GetString()!).ToList();
        }

        internal static List<string> Ids(JsonElement page, string? nested = null)
        {
            return page.GetProperty("objects").EnumerateArray().Select(o => nested == null ? Api.Str(o, "id") : Api.Str(o.GetProperty(nested), "id")).ToList();
        }

        internal static int Count(JsonElement page)
        {
            return page.GetProperty("objects").GetArrayLength();
        }
    }
}
