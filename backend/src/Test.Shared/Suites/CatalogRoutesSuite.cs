namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using Touchstone.Core;
    using TrellisKit.Core.Models;

    /// <summary>HTTP contract coverage for bounded configuration catalogs and member collection retrievals.</summary>
    public static class CatalogRoutesSuite
    {
        /// <summary>Suite identity.</summary>
        public const string Id = "CatalogRoutes";
        /// <summary>Build the suite.</summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build() => new TestSuiteDescriptor(Id, "Catalog enumeration contracts", new[]
        {
            SuiteCases.Http(Id, "AdminCatalogs.PageAndFilter", "Admin catalogs expose GET and POST standard pages with scoped cursors, dates, metadata and search", async (f, ct) =>
            {
                foreach (string path in new[] { "/api/v1/settings", "/api/v1/admin/property/settings", "/api/v1/admin/email/templates", "/api/v1/admin/email/staff-events", "/api/v1/admin/property/domains", "/api/v1/admin/property/apps", "/api/v1/admin/documents", "/api/v1/admin/directory/settings" })
                {
                    JsonElement page = await Api.ExpectAsync(f, HttpMethod.Get, path + "?maxResults=1", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(page.GetProperty("objects").GetArrayLength() <= 1, path + " is bounded");
                    Check.Equal(1, page.GetProperty("maxResults").GetInt32(), path + " maxResults");
                    Check.True(page.TryGetProperty("endOfResults", out _) && page.TryGetProperty("totalRecords", out _), path + " standard envelope");
                    JsonElement post = await Api.ExpectAsync(f, HttpMethod.Post, path + "/enumerate", HttpFixture.AdminToken, new { maxResults = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(page.GetProperty("objects").ToString(), post.GetProperty("objects").ToString(), path + " GET/POST parity");
                    foreach (JsonElement item in page.GetProperty("objects").EnumerateArray())
                        Check.True(item.TryGetProperty("labels", out _) && item.TryGetProperty("tags", out _), path + " item metadata");
                    if (!page.GetProperty("endOfResults").GetBoolean())
                    {
                        string cursor = Api.Str(page, "continuationToken");
                        JsonElement next = await Api.ExpectAsync(f, HttpMethod.Post, path + "/enumerate", HttpFixture.AdminToken, new { maxResults = 1, continuationToken = cursor }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(next.GetProperty("objects").ToString() != page.GetProperty("objects").ToString(), path + " advances");
                        await Api.ExpectErrorAsync(f, HttpMethod.Post, path + "/enumerate", HttpFixture.AdminToken, new { maxResults = 1, continuationToken = cursor, search = "changed" }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                    }
                    JsonElement missing = await Api.ExpectAsync(f, HttpMethod.Post, path + "/enumerate", HttpFixture.AdminToken, new { labels = new[] { "absent-catalog-label" }, tags = new { absent = "*" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(0, missing.GetProperty("objects").GetArrayLength(), path + " metadata filters apply");
                    await Api.ExpectErrorAsync(f, HttpMethod.Post, path + "/enumerate", HttpFixture.AdminToken, new { filters = new { unknown = true } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                }
                JsonElement search = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/property/settings?search=email.fromName", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.True(search.GetProperty("objects").EnumerateArray().Any(x => Api.Str(x, "key") == "email.fromName"), "Setting search");
                JsonElement category = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/email/templates/enumerate", HttpFixture.AdminToken, new { maxResults = 1000, filters = new { category = "Staff" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.True(category.GetProperty("objects").GetArrayLength() > 0 && category.GetProperty("objects").EnumerateArray().All(x => Api.Str(x, "category") == "Staff"), "Template category filters before totals and paging");
                await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/email/templates/enumerate", HttpFixture.AdminToken, new { filters = new { category = "Unknown" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
                JsonElement dates = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/email/templates/enumerate", HttpFixture.AdminToken, new { createdAfterUtc = "2000-01-01T00:00:00Z" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal(0, dates.GetProperty("objects").GetArrayLength(), "Immutable declaration timestamps are epoch");
            }),
            SuiteCases.Http(Id, "InstallCounts.NoTopTenTruncation", "Signing key install counts page past the old top-ten cap with exact totals and filters", async (f, ct) =>
            {
                DateTime first = DateTime.UtcNow.AddDays(-2);
                for (int i = 0; i < 31; i++)
                    await f.Database.LiveUpdates.UpsertInstallAsync(new UpdateInstall { Id = "uin_" + Guid.NewGuid().ToString("N"), InstallIdHash = Guid.NewGuid().ToString("N"), Platform = "ios", Channel = "production", RuntimeVersion = "1", KeyId = i == 30 ? null : "key-" + (i / 2).ToString("D2"), FirstSeenUtc = first, LastCheckUtc = DateTime.UtcNow }, ct).ConfigureAwait(false);
                const string path = "/api/v1/admin/updates/signing-keys/installs/enumerate";
                string? cursor = null;
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, path, HttpFixture.AdminToken, new { maxResults = 4, continuationToken = cursor }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(16L, page.GetProperty("totalRecords").GetInt64(), "All grouped keys counted, including missing embedded key");
                    Check.True(page.GetProperty("objects").GetArrayLength() <= 4, "SQL page bounded");
                    foreach (JsonElement item in page.GetProperty("objects").EnumerateArray()) Check.True(seen.Add(Api.Str(item, "key")), "No duplicate cursor row");
                    cursor = page.GetProperty("continuationToken").ValueKind == JsonValueKind.Null ? null : Api.Str(page, "continuationToken");
                } while (cursor != null);
                Check.Equal(16, seen.Count, "Every key reachable beyond old ten-row preview");
                JsonElement searched = await Api.ExpectAsync(f, HttpMethod.Post, path, HttpFixture.AdminToken, new { search = "key-14", createdAfterUtc = first.AddDays(-1), createdBeforeUtc = first.AddDays(1) }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal(1, searched.GetProperty("objects").GetArrayLength(), "Search and creation window");
                JsonElement metadata = await Api.ExpectAsync(f, HttpMethod.Post, path, HttpFixture.AdminToken, new { labels = new[] { "absent" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal(0, metadata.GetProperty("objects").GetArrayLength(), "Empty aggregate metadata never silently ignores filters");
            }),
            SuiteCases.Http(Id, "MemberCatalogs.ContextAndScope", "Member preferences and reactions page while preserving scalar context; cursors cannot cross users", async (f, ct) =>
            {
                TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                TestMember other = await Api.MemberAsync(f, ct).ConfigureAwait(false);
                const string preferences = "/api/v1/me/notification-preferences";
                JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, preferences + "/enumerate", member.AccessToken, new { maxResults = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal(1, page.GetProperty("objects").GetArrayLength(), "One category");
                Check.True(page.TryGetProperty("pushPaused", out _) && page.TryGetProperty("notificationHours", out _), "Preference context");
                await Api.ExpectErrorAsync(f, HttpMethod.Post, preferences + "/enumerate", other.AccessToken, new { maxResults = 1, continuationToken = Api.Str(page, "continuationToken") }, HttpStatusCode.BadRequest, "InvalidContinuationToken", ct).ConfigureAwait(false);
                JsonElement reactions = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/reactions/set/enumerate", member.AccessToken, new { maxResults = 1 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal(1, reactions.GetProperty("objects").GetArrayLength(), "One reaction");
                Check.True(reactions.TryGetProperty("likeKey", out _), "Reaction context");
                await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/admin/email/templates/enumerate", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
                await Api.ExpectErrorAsync(f, HttpMethod.Post, "/api/v1/media/enumerate", member.AccessToken, new { filters = new { ids = new object[] { 1 } } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            })
        });
    }
}
