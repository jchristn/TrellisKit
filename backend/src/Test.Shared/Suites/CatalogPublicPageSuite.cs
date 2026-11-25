namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// Server-rendered catalog entry pages and sitemaps (docs/CATALOG_PUBLIC_PAGES.md): canonical metadata, escaping,
    /// private data omission, relationship withdrawal, the configurable path word, and the features.catalog gate.
    /// </summary>
    public static class CatalogPublicPageSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "CatalogPublicPages";

        #endregion

        #region Private-Members

        private const string _Resolve = "/api/v1/public/pages/resolve/";
        private static readonly Regex _JsonLd = new Regex("<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Catalog public pages", new List<TestCaseDescriptor>
            {
                Case("CatalogPublicPages.Config", "The catalog path defaults to catalog, maps to the Catalog kind, and cannot shadow another path", ConfigAsync),
                Case("CatalogPublicPages.Page", "Published catalog pages have canonical metadata, safe instruction text, CreativeWork and breadcrumbs; drafts and their graph links remain private", CatalogPageAsync),
                Case("CatalogPublicPages.Sitemap", "Catalog sitemap is bounded and tracks publication, changed timestamps, withdrawal, and deletion", CatalogSitemapAsync),
                Case("CatalogPublicPages.CustomPath", "Configured catalog paths determine canonical links and sitemap URLs", CatalogCustomPathAsync, p => p.PublicPages = new PropertyPublicPages { CatalogPath = "techniques" }),
                Case("CatalogPublicPages.Disabled", "Turning features.catalog off removes catalog pages and sitemap entries", CatalogDisabledAsync, p => p.Features.Catalog = false)
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body, Action<PropertyConfiguration>? customize = null)
        {
            return SuiteCases.Http(Id, caseId, name, p =>
            {
                p.Features.Catalog = true;
                customize?.Invoke(p);
            }, (f, ct) =>
            {
                f.Settings.RateLimits.PublicPagesPerMinute = 100000;
                return body(f, ct);
            });
        }

        private static Task ConfigAsync(HttpFixture f, CancellationToken ct)
        {
            PropertyPublicPages defaults = new PropertyConfiguration().EffectivePublicPages;
            Check.Equal("catalog", defaults.CatalogPath, "Default catalog path");
            Check.Equal(PublicPageKindEnum.Catalog, defaults.KindOf("catalog"), "Catalog path kind");
            PropertyConfiguration property = TestProperty.LoadSample();
            property.PublicPages = new PropertyPublicPages { CatalogPath = "profiles" };
            Check.True(PropertyValidator.Validate(property).Any(p => p.StartsWith("publicPages.catalogPath", StringComparison.Ordinal) && p.Contains("differ", StringComparison.Ordinal)), "Catalog cannot shadow another public page path");
            property.PublicPages = new PropertyPublicPages { CatalogPath = "admin" };
            Check.True(PropertyValidator.Validate(property).Any(p => p.StartsWith("publicPages.catalogPath", StringComparison.Ordinal) && p.Contains("reserved", StringComparison.Ordinal)), "Catalog path cannot be reserved");
            return Task.CompletedTask;
        }

        private static async Task<ApiResult> GetAsync(HttpFixture f, string path, CancellationToken ct)
        {
            // Redirects are checked, not followed (the fixture's client follows them).
            using (HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = false })
            using (HttpClient client = new HttpClient(handler) { BaseAddress = f.Client.BaseAddress })
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, path))
            {
                f.Decorate(request);
                using (HttpResponseMessage response = await client.SendAsync(request, ct).ConfigureAwait(false))
                {
                    ApiResult result = new ApiResult
                    {
                        Status = response.StatusCode,
                        Text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false),
                        ContentType = response.Content.Headers.ContentType?.ToString()
                    };
                    foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers) result.Headers[header.Key] = String.Join(",", header.Value);
                    foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers) if (!result.Headers.ContainsKey(header.Key)) result.Headers[header.Key] = String.Join(",", header.Value);
                    return result;
                }
            }
        }

        private static async Task<string> PageAsync(HttpFixture f, string word, string slug, CancellationToken ct)
        {
            ApiResult page = await GetAsync(f, _Resolve + word + "/" + slug, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, page.Status, "Page " + word + "/" + slug);
            Check.True((page.ContentType ?? String.Empty).StartsWith("text/html", StringComparison.Ordinal), "HTML content type");
            return page.Text;
        }

        private static async Task NotFoundAsync(HttpFixture f, string word, string slug, string what, CancellationToken ct)
        {
            ApiResult page = await GetAsync(f, _Resolve + word + "/" + slug, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.NotFound, page.Status, what);
            Check.True(page.Text.Contains("noindex", StringComparison.Ordinal), what + ": the not found page is noindex");
            Check.True(!page.Headers.ContainsKey("X-Accel-Redirect"), what + ": a page path never passes through");
        }

        private static JsonElement JsonLd(string html)
        {
            Match m = _JsonLd.Match(html);
            Check.True(m.Success, "JSON-LD script present");
            using (JsonDocument doc = JsonDocument.Parse(m.Groups[1].Value)) return doc.RootElement.Clone();
        }

        private static string? Meta(string html, string key)
        {
            Match m = Regex.Match(html, "<meta (?:property|name)=\"" + Regex.Escape(key) + "\" content=\"([^\"]*)\">", RegexOptions.CultureInvariant);
            return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
        }

        private static async Task<CatalogEntry> CatalogEntryAsync(HttpFixture f, string slug, string status, CancellationToken ct)
        {
            CatalogEntry entry = new CatalogEntry
            {
                Id = IdGenerator.Generate("cex_"), Name = "Example movement", Slug = slug, Status = status,
                Summary = "A reviewed movement description.", Instructions = new List<string> { "Begin in a stable position.", "Move <slowly> & steadily." },
                ReviewNote = "PRIVATE_REVIEW", ReviewedByUserId = "PRIVATE_REVIEWER", CreatedByUserId = "PRIVATE_AUTHOR",
                PublishedUtc = status == "Published" ? DateTime.UtcNow.AddDays(-2) : null, UpdatedUtc = DateTime.UtcNow.AddDays(-1),
                Sources = new List<CatalogSource> { new CatalogSource { Title = "Example reference", Url = "https://example.test/reference", License = "CC-BY-4.0", Attribution = "Example contributor <credit>", Model = "PRIVATE_MODEL", Method = "Model" } }
            };
            Check.True(await f.Database.ExerciseCatalog.SaveAsync(entry, null, true, null, ct).ConfigureAwait(false), "Catalog fixture saved");
            return entry;
        }

        private static async Task CatalogPageAsync(HttpFixture f, CancellationToken ct)
        {
            CatalogEntry published = await CatalogEntryAsync(f, "published-movement", "Published", ct).ConfigureAwait(false);
            CatalogEntry target = await CatalogEntryAsync(f, "related-movement", "Published", ct).ConfigureAwait(false);
            CatalogEntry draft = await CatalogEntryAsync(f, "private-draft", "Draft", ct).ConfigureAwait(false);
            List<CatalogRelationship> edges = new List<CatalogRelationship>
            {
                new CatalogRelationship { Id = IdGenerator.Generate("cer_"), TargetId = target.Id, Kind = "Progression", Note = "A linked progression." },
                new CatalogRelationship { Id = IdGenerator.Generate("cer_"), TargetId = draft.Id, Kind = "Substitution", Note = "PRIVATE_EDGE" }
            };
            Check.True(await f.Database.ExerciseCatalog.SaveAsync(published, published.Revision, false, edges, ct).ConfigureAwait(false), "Catalog graph saved");
            string html = await PageAsync(f, "catalog", published.Slug, ct).ConfigureAwait(false);
            ApiResult fresh = await GetAsync(f, _Resolve + "catalog/" + published.Slug, ct).ConfigureAwait(false);
            Check.True(fresh.Headers["Cache-Control"].Contains("must-revalidate", StringComparison.Ordinal), "Catalog cache revalidates publication state");
            string url = f.Services.PublicPages.CatalogUrl(published)!;
            Check.Equal(url, Meta(html, "og:url"), "Open Graph canonical URL");
            Check.True(html.Contains("rel=\"canonical\" href=\"" + url + "\"", StringComparison.Ordinal), "Canonical link");
            Check.Equal("article", Meta(html, "og:type"), "Article metadata");
            Check.True(html.Contains("<ol>", StringComparison.Ordinal) && html.Contains("Move &lt;slowly&gt; &amp; steadily.", StringComparison.Ordinal), "Instruction sequence is escaped");
            Check.True(html.Contains("/app/catalog/" + published.Slug, StringComparison.Ordinal) && html.Contains("/catalog/" + target.Slug, StringComparison.Ordinal), "App and public related-entry links");
            Check.True(html.Contains("CC-BY-4.0", StringComparison.Ordinal) && html.Contains("Example contributor &lt;credit&gt;", StringComparison.Ordinal), "License and attribution are visible");
            Check.True(!html.Contains("PRIVATE_", StringComparison.Ordinal) && !html.Contains(draft.Slug, StringComparison.Ordinal) && !html.Contains(draft.Id, StringComparison.Ordinal), "Private review, generator, and draft relationship data omitted");
            JsonElement graph = JsonLd(html).GetProperty("@graph");
            Check.Equal("CreativeWork", Api.Str(graph[0], "@type"), "Instructional catalog article is CreativeWork, not a prescribed routine");
            Check.Equal("BreadcrumbList", Api.Str(graph[1], "@type"), "Breadcrumb schema");
            Check.Equal(2, graph[1].GetProperty("itemListElement").GetArrayLength(), "Home and current page breadcrumbs");
            Check.True(!graph[0].TryGetProperty("aggregateRating", out _) && !graph[0].TryGetProperty("review", out _), "No invented reviews");
            await NotFoundAsync(f, "catalog", draft.Slug, "Draft slug hidden", ct).ConfigureAwait(false);
            await NotFoundAsync(f, "catalog", draft.Id, "Draft identifier hidden", ct).ConfigureAwait(false);
            ApiResult canonical = await GetAsync(f, _Resolve + "catalog/" + published.Id, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.MovedPermanently, canonical.Status, "Published identifier redirects to stable slug");
            Check.True(canonical.Headers["Location"].EndsWith("/catalog/" + published.Slug, StringComparison.Ordinal), "Redirect uses canonical slug");
            foreach (string status in new[] { "InReview", "Rejected", "Archived" })
            {
                draft.Status = status;
                await f.Database.ExerciseCatalog.SaveAsync(draft, draft.Revision, false, null, ct).ConfigureAwait(false);
                await NotFoundAsync(f, "catalog", draft.Id, status + " identifier hidden", ct).ConfigureAwait(false);
            }
            target.Status = "Draft";
            await f.Database.ExerciseCatalog.SaveAsync(target, target.Revision, false, null, ct).ConfigureAwait(false);
            string withdrawn = await PageAsync(f, "catalog", published.Slug, ct).ConfigureAwait(false);
            Check.True(!withdrawn.Contains("/catalog/" + target.Slug, StringComparison.Ordinal), "Withdrawing a related target removes its public link");
        }

        private static async Task CatalogSitemapAsync(HttpFixture f, CancellationToken ct)
        {
            CatalogEntry entry = await CatalogEntryAsync(f, "sitemap-movement", "Draft", ct).ConfigureAwait(false);
            const string indexPath = "/api/v1/public/pages/sitemap.xml";
            const string childPath = "/api/v1/public/pages/sitemaps/catalog-1.xml";
            Check.True(!(await GetAsync(f, indexPath, ct).ConfigureAwait(false)).Text.Contains("sitemap-catalog", StringComparison.Ordinal), "No catalog child for drafts");
            entry.Status = "Published";
            entry.PublishedUtc = DateTime.UtcNow;
            entry.UpdatedUtc = DateTime.UtcNow;
            await f.Database.ExerciseCatalog.SaveAsync(entry, entry.Revision, false, null, ct).ConfigureAwait(false);
            Check.True((await GetAsync(f, indexPath, ct).ConfigureAwait(false)).Text.Contains("sitemap-catalog-1.xml", StringComparison.Ordinal), "Publication adds child sitemap");
            ApiResult sitemap = await GetAsync(f, childPath, ct).ConfigureAwait(false);
            Check.True(sitemap.Headers["Cache-Control"].Contains("must-revalidate", StringComparison.Ordinal), "Catalog sitemap cache revalidates withdrawal");
            Check.True(sitemap.Text.Contains("/catalog/" + entry.Slug, StringComparison.Ordinal) && sitemap.Text.Contains(entry.UpdatedUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), StringComparison.Ordinal), "Current slug and changed-at timestamp");
            entry.UpdatedUtc = entry.UpdatedUtc.AddHours(1);
            await f.Database.ExerciseCatalog.SaveAsync(entry, entry.Revision, false, null, ct).ConfigureAwait(false);
            Check.True((await GetAsync(f, childPath, ct).ConfigureAwait(false)).Text.Contains(entry.UpdatedUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), StringComparison.Ordinal), "Changed-at tracks catalog edits");
            Check.Equal(HttpStatusCode.NotFound, (await GetAsync(f, "/api/v1/public/pages/sitemaps/catalog-2.xml", ct).ConfigureAwait(false)).Status, "Empty later page is 404");
            Check.Equal(HttpStatusCode.NotFound, (await GetAsync(f, "/api/v1/public/pages/sitemaps/catalog-1001.xml", ct).ConfigureAwait(false)).Status, "Bounded number of files");
            List<PublicPageEntry> rows = await f.Database.PublicPages.SitemapPageAsync(PublicPageKindEnum.Catalog, DateTime.UtcNow, 0, 1, ct).ConfigureAwait(false);
            Check.Equal(1, rows.Count, "Explicit page bound");
            Check.Equal(0, (await f.Database.PublicPages.SitemapPageAsync(PublicPageKindEnum.Catalog, DateTime.UtcNow, 1, 1, ct).ConfigureAwait(false)).Count, "Stable offset ends");
            entry.Status = "Draft";
            await f.Database.ExerciseCatalog.SaveAsync(entry, entry.Revision, false, null, ct).ConfigureAwait(false);
            Check.True(!(await GetAsync(f, childPath, ct).ConfigureAwait(false)).Text.Contains(entry.Slug, StringComparison.Ordinal), "Withdrawal removes URL");
            entry.Status = "Published";
            entry.DeletedUtc = DateTime.UtcNow;
            await f.Database.ExerciseCatalog.SaveAsync(entry, entry.Revision, false, null, ct).ConfigureAwait(false);
            await NotFoundAsync(f, "catalog", entry.Id, "Deleted identifier hidden", ct).ConfigureAwait(false);
            Check.True(!(await GetAsync(f, childPath, ct).ConfigureAwait(false)).Text.Contains(entry.Slug, StringComparison.Ordinal), "Deleted published entry excluded");
        }

        private static async Task CatalogCustomPathAsync(HttpFixture f, CancellationToken ct)
        {
            CatalogEntry entry = await CatalogEntryAsync(f, "custom-movement", "Published", ct).ConfigureAwait(false);
            string html = await PageAsync(f, "techniques", entry.Slug, ct).ConfigureAwait(false);
            Check.True(Meta(html, "og:url")!.EndsWith("/techniques/" + entry.Slug, StringComparison.Ordinal), "Configured canonical path");
            ApiResult old = await GetAsync(f, _Resolve + "catalog/" + entry.Slug, ct).ConfigureAwait(false);
            Check.True(old.Headers.ContainsKey("X-Accel-Redirect"), "Default path is no longer a catalog page");
            ApiResult sitemap = await GetAsync(f, "/api/v1/public/pages/sitemaps/catalog-1.xml", ct).ConfigureAwait(false);
            Check.True(sitemap.Text.Contains("/techniques/" + entry.Slug, StringComparison.Ordinal), "Stable sitemap filename with configured page URL");
        }

        private static async Task CatalogDisabledAsync(HttpFixture f, CancellationToken ct)
        {
            CatalogEntry entry = await CatalogEntryAsync(f, "disabled-movement", "Published", ct).ConfigureAwait(false);
            Check.True(f.Services.PublicPages.CatalogUrl(entry) == null, "Disabled feature has no public URL");
            ApiResult page = await GetAsync(f, _Resolve + "catalog/" + entry.Slug, ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.NotFound, page.Status, "No rendered catalog when the catalog is disabled");
            Check.True(!page.Text.Contains(entry.Summary!, StringComparison.Ordinal), "Disabled content not rendered");
            Check.Equal(HttpStatusCode.NotFound, (await GetAsync(f, "/api/v1/public/pages/sitemaps/catalog-1.xml", ct).ConfigureAwait(false)).Status, "Disabled child sitemap unavailable");
            Check.True(!(await GetAsync(f, "/api/v1/public/pages/sitemap.xml", ct).ConfigureAwait(false)).Text.Contains("sitemap-catalog", StringComparison.Ordinal), "Disabled sitemap omitted");
        }

        #endregion
    }
}
