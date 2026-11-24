namespace Test.Shared
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
    using Test.Shared.Suites;

    /// <summary>Test consumers traverse real enumeration pages before reconstructing business chart assertions.</summary>
    internal static class MetricEnumerationTestKit
    {
        internal static async Task<JsonElement> DailyAsync(HttpFixture fixture, string path, string token, CancellationToken cancellation)
        {
            JsonElement result = await PagesAsync(fixture, path, token, cancellation).ConfigureAwait(false);
            Dictionary<string, object?> view = Context(result);
            foreach (IGrouping<string, JsonElement> series in result.GetProperty("objects").EnumerateArray().GroupBy(r => Api.Str(r, "series")))
                view[series.Key] = series.OrderBy(r => Api.Str(r, "date"), StringComparer.Ordinal).Select(r => new { date = Api.Str(r, "date"), count = r.GetProperty("value").GetInt64() }).ToList();
            return JsonSerializer.SerializeToElement(view);
        }
        internal static async Task<JsonElement> UsageAsync(HttpFixture fixture, string path, string token, CancellationToken cancellation)
        {
            JsonElement result = await PagesAsync(fixture, path, token, cancellation).ConfigureAwait(false);
            Dictionary<string, object?> view = Context(result); List<JsonElement> rows = result.GetProperty("objects").EnumerateArray().ToList();
            view["buckets"] = rows.GroupBy(r => Api.Str(r, "startUtc")).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new { startUtc = Api.Str(g.First(), "startUtc"), endUtc = Api.Str(g.First(), "endUtc"), label = Api.Str(g.First(), "label") }).ToList();
            view["meters"] = rows.GroupBy(r => Api.Str(r, "meter")).Select(g =>
            {
                Dictionary<string, object?> meter = Context(g.First());
                meter["values"] = g.OrderBy(r => Api.Str(r, "startUtc"), StringComparer.Ordinal).Select(r => r.GetProperty("value").Clone()).ToList(); return meter;
            }).ToList();
            return JsonSerializer.SerializeToElement(view);
        }
        private static Dictionary<string, object?> Context(JsonElement result) => result.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        private static async Task<JsonElement> PagesAsync(HttpFixture fixture, string path, string token, CancellationToken cancellation)
        {
            List<JsonElement> objects = new List<JsonElement>(); JsonElement first = default; string? cursor = null; int pages = 0;
            do
            {
                string uri = path + (path.Contains("?", StringComparison.Ordinal) ? "&" : "?") + "maxResults=100" + (cursor == null ? String.Empty : "&continuationToken=" + Uri.EscapeDataString(cursor));
                JsonElement page = path.StartsWith("/api/v1/platform/", StringComparison.Ordinal)
                    ? await MultiPropertyKit.PlatformExpectAsync(fixture, HttpMethod.Get, uri, token, null, HttpStatusCode.OK, cancellation).ConfigureAwait(false)
                    : await Api.ExpectAsync(fixture, HttpMethod.Get, uri, token, null, HttpStatusCode.OK, cancellation).ConfigureAwait(false);
                if (pages++ == 0) first = page.Clone();
                Check.True(page.GetProperty("objects").GetArrayLength() <= page.GetProperty("maxResults").GetInt32(), "Every transport page obeys its maximum");
                objects.AddRange(page.GetProperty("objects").EnumerateArray().Select(r => r.Clone()));
                cursor = page.GetProperty("continuationToken").ValueKind == JsonValueKind.String ? page.GetProperty("continuationToken").GetString() : null;
                Check.True(pages < 200, "Bounded test traversal");
            } while (cursor != null);
            Check.Equal(first.GetProperty("totalRecords").GetInt64(), (long)objects.Count, "Enumeration traversal returns every observation exactly once");
            Dictionary<string, object?> combined = Context(first); combined["objects"] = objects; return JsonSerializer.SerializeToElement(combined);
        }
    }
}
