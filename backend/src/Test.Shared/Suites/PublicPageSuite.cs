namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
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
    /// Public landing pages (docs/PHASE4_CONTRACT.md section 18): slug rules, generation, uniqueness, and redirects;
    /// opt-in profile pages (off by default, never for opted-out, banned, or deleted members, members-only fields kept
    /// off); venue pages for every listed venue with address visibility and no member reviews; Public-only event pages;
    /// HTML escaping and JSON-LD validity; the sitemap and its bounds; robots.txt; the configurable path words and the
    /// pass-through of other paths to the website.
    /// </summary>
    public static class PublicPageSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "PublicPages";

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
            return new TestSuiteDescriptor(Id, "Public landing pages (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Unit(Id, "PublicPages.SlugRules", "Slugs are 3 to 80 lowercase letters and digits with single hyphens and not reserved; derived slugs join name, locality, and region; suffixes stay within 80 characters", SlugRulesAsync),
                SuiteCases.Unit(Id, "PublicPages.ConfigValidation", "publicPages path words default to venues, events, and profiles and must be valid, distinct, and not reserved", ConfigAsync),
                Case("PublicPages.VenuePage", "Every listed venue (any audience) gets a branded page with title, canonical, Open Graph, and SportsActivityLocation JSON-LD; member reviews never appear; closed venues are 404; payloads carry publicUrl", VenuePageAsync),
                Case("PublicPages.VenueAddressVisibility", "Exact shows the street address and coordinates; Approximate shows the locality with rounded coordinates; LocalityOnly shows the locality without coordinates", AddressVisibilityAsync),
                Case("PublicPages.SlugsAndRedirects", "Slug validation (400), uniqueness (409), the slug check route, and 301 redirects from a previous slug for venues, events, and profiles", SlugsAsync),
                Case("PublicPages.EventPages", "Only Public, published (or canceled) events get a page with Event JSON-LD and eventStatus; Members events and drafts are 404; hosts may change the slug", EventPagesAsync),
                Case("PublicPages.ProfilePages", "Profile pages are off by default; opting in publishes only public information (no email, no members-only fields); banned and deleted members lose the page; admins may set it", ProfilePagesAsync, p =>
                {
                    ProfileField? city = p.ProfileFields?.FirstOrDefault(x => x.Key == "city");
                    if (city != null) city.Visibility = ProfileVisibilityEnum.Members;
                }),
                Case("PublicPages.Escaping", "Names with markup are HTML-encoded in the page and JSON-LD stays valid and cannot close its script element", EscapingAsync),
                Case("PublicPages.Sitemap", "The sitemap index lists the website pages sitemap and one child per kind; children list current slugs with lastmod; Members events and ended events drop out; queries are bounded and paged", SitemapAsync),
                Case("PublicPages.RobotsAndPassThrough", "robots.txt disallows /app, /admin, and /api and names the sitemap; paths that are not page paths answer with X-Accel-Redirect to the website shell", RobotsAsync),
                Case("PublicPages.CustomPaths", "A property's own path words route pages and URLs; the engine default word then passes through", CustomPathsAsync, p => p.PublicPages = new PropertyPublicPages { VenuesPath = "places", EventsPath = "happenings", ProfilesPath = "people" }),
                Case("PublicPages.Disabled", "publicPages.enabled false: no pages, no publicUrl, an empty sitemap index apart from the website pages, and 404 NotAvailable on the settings route", DisabledAsync, p => p.PublicPages = new PropertyPublicPages { Enabled = false })
            });
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string name, Func<HttpFixture, CancellationToken, Task> body, Action<PropertyConfiguration>? customize = null)
        {
            return SuiteCases.Http(Id, caseId, name, p => customize?.Invoke(p), (f, ct) =>
            {
                DirectoryTestKit.Relax(f);
                EventTestKit.Relax(f);
                f.Settings.RateLimits.PublicPagesPerMinute = 100000;
                f.Settings.Directory.ReviewsPerDay = 1000;
                f.Settings.Directory.NewAccountReviewsPerDay = 1000;
                f.Settings.RateLimits.EditsPerHour = 10000;
                return body(f, ct);
            });
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

        private static Task SlugRulesAsync(CancellationToken ct)
        {
            Check.Equal((string?)null, PublicSlugRules.Problem("example-club-exampleville-ex"), "Valid slug");
            Check.Equal("required", PublicSlugRules.Problem("  "), "Empty");
            Check.Equal("length", PublicSlugRules.Problem("ab"), "Too short");
            Check.Equal("length", PublicSlugRules.Problem(new string('a', 81)), "Too long");
            Check.Equal("format", PublicSlugRules.Problem("double--hyphen"), "Double hyphen");
            Check.Equal("format", PublicSlugRules.Problem("-lead"), "Leading hyphen");
            Check.Equal("format", PublicSlugRules.Problem("Upper"), "Upper case");
            Check.Equal("format", PublicSlugRules.Problem("under_score"), "Underscore");
            Check.Equal("reserved", PublicSlugRules.Problem("admin"), "Reserved");
            Check.Equal("example-club-exampleville-ex", PublicSlugRules.Derive("venue", "Example Club", "Exampleville", "EX"), "Name, locality, region");
            Check.Equal("cafe-co", PublicSlugRules.Derive("venue", "Caf\u00e9 & Co.", null, " "), "Accents and symbols");
            Check.Equal("venue", PublicSlugRules.Derive("venue", "!!!"), "Fallback when nothing is left");
            Check.Equal("ab-venue", PublicSlugRules.Derive("venue", "AB"), "Too short gets the fallback appended");
            Check.Equal("admin-venue", PublicSlugRules.Derive("venue", "Admin"), "Reserved gets the fallback appended");
            string longSlug = PublicSlugRules.Derive("venue", String.Join(" ", Enumerable.Repeat("word", 40)));
            Check.True(longSlug.Length <= 80 && PublicSlugRules.IsValid(longSlug), "Long text is cut at a hyphen");
            string suffixed = PublicSlugRules.WithSuffix(new string('a', 80), 12);
            Check.True(suffixed.Length <= 80 && suffixed.EndsWith("-12", StringComparison.Ordinal), "Suffix stays within 80");
            return Task.CompletedTask;
        }

        private static Task ConfigAsync(CancellationToken ct)
        {
            PropertyPublicPages defaults = new PropertyConfiguration().EffectivePublicPages;
            Check.Equal("venues", defaults.VenuesPath, "Default venues path");
            Check.Equal("events", defaults.EventsPath, "Default events path");
            Check.Equal("profiles", defaults.ProfilesPath, "Default profiles path");
            Check.True(defaults.Enabled, "On by default");
            Check.Equal(PublicPageKindEnum.Venue, defaults.KindOf("venues"), "KindOf");
            Check.Equal((PublicPageKindEnum?)null, defaults.KindOf("lessons"), "Other words are not pages");
            PropertyConfiguration property = TestProperty.LoadSample();
            Check.Equal(0, PropertyValidator.Validate(property).Count(p => p.StartsWith("publicPages", StringComparison.Ordinal)), "Sample has no publicPages problems");
            property.PublicPages = new PropertyPublicPages { VenuesPath = "app", EventsPath = "Bad Word", ProfilesPath = "places" };
            List<string> problems = PropertyValidator.Validate(property).Where(p => p.StartsWith("publicPages", StringComparison.Ordinal)).ToList();
            Check.True(problems.Any(p => p.StartsWith("publicPages.venuesPath", StringComparison.Ordinal) && p.Contains("reserved", StringComparison.Ordinal)), "Reserved path word");
            Check.True(problems.Any(p => p.StartsWith("publicPages.eventsPath", StringComparison.Ordinal)), "Format");
            property.PublicPages = new PropertyPublicPages { VenuesPath = "places", EventsPath = "places", ProfilesPath = "people" };
            Check.True(PropertyValidator.Validate(property).Any(p => p.StartsWith("publicPages.eventsPath", StringComparison.Ordinal) && p.Contains("differ", StringComparison.Ordinal)), "Distinct path words");
            return Task.CompletedTask;
        }

        private static async Task VenuePageAsync(HttpFixture f, CancellationToken ct)
        {
            JsonElement venue = await DirectoryTestKit.VenueAsync(f, "Example Club Downtown", ct, 40.712776, -74.005974, new
            {
                affiliation = "Example Network",
                summary = "A friendly club.",
                description = "Open to visitors every weekend.",
                address = new { line1 = "100 Example Street", locality = "Exampleville", region = "EX", postalCode = "00000", countryCode = "US" },
                email = "hidden-venue-email@example.test",
                websiteUrl = "https://example.test",
                socialLinks = new[] { new { kind = "Instagram", url = "https://example.test/club" } },
                scheduleText = "Mon to Fri 6 PM",
                visitorPolicy = "Welcome",
                audience = "Members"
            }).ConfigureAwait(false);
            string id = Api.Str(venue, "id");
            string slug = Api.Str(venue, "slug");
            Check.Equal("example-club-downtown-exampleville-ex", slug, "Default slug");
            Check.True(Api.Str(venue, "publicUrl").EndsWith("/venues/" + slug, StringComparison.Ordinal), "Admin payload publicUrl");
            await DirectoryTestKit.InstructorAsync(f, "Coach Example", ct, new { venueIds = new[] { id } }).ConfigureAwait(false);
            TestMember reviewer = await SafetyHelpers.MemberAsync(f, ct, "Review Writer").ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/venues/" + id + "/reviews", reviewer.AccessToken, new { rating = 4, body = "Secretreviewtext about the mats." }, HttpStatusCode.Created, ct).ConfigureAwait(false);

            string html = await PageAsync(f, "venues", slug, ct).ConfigureAwait(false);
            Check.True(html.StartsWith("<!DOCTYPE html>", StringComparison.Ordinal), "Complete document");
            Check.True(html.Contains("<title>Example Club Downtown, Exampleville | " + f.Property.Name + "</title>", StringComparison.Ordinal), "Title");
            Check.True(html.Contains("<link rel=\"canonical\" href=\"" + Api.Str(venue, "publicUrl") + "\">", StringComparison.Ordinal), "Canonical from web.publicBaseUrl");
            Check.Equal("A friendly club.", Meta(html, "description"), "Meta description");
            Check.Equal(Api.Str(venue, "publicUrl"), Meta(html, "og:url"), "og:url");
            Check.True(!String.IsNullOrEmpty(Meta(html, "og:image")), "og:image falls back to the logo");
            Check.Equal("summary_large_image", Meta(html, "twitter:card"), "Twitter card");
            Check.True(html.Contains("100 Example Street", StringComparison.Ordinal), "Exact address");
            Check.True(html.Contains("Coach Example", StringComparison.Ordinal), "Linked instructors");
            Check.True(html.Contains("Mon to Fri 6 PM", StringComparison.Ordinal), "Schedule");
            Check.True(html.Contains("Visitors welcome", StringComparison.Ordinal), "Visitor policy");
            Check.True(html.Contains("4.0 out of 5 from 1 rating", StringComparison.Ordinal), "Rating average and count");
            Check.True(!html.Contains("Secretreviewtext", StringComparison.Ordinal), "No member review text");
            Check.True(!html.Contains("hidden-venue-email", StringComparison.Ordinal), "No venue email");
            Check.True(html.Contains("/app/venues/" + id, StringComparison.Ordinal), "Call to action into the member app");
            Check.True(html.Contains("/sign-up", StringComparison.Ordinal) || html.Contains("/join", StringComparison.Ordinal), "Join link");
            Check.True(html.Contains("prefers-color-scheme: dark", StringComparison.Ordinal), "Light and dark brand colors");
            JsonElement ld = JsonLd(html);
            Check.Equal("SportsActivityLocation", Api.Str(ld, "@type"), "JSON-LD type");
            Check.Equal("Example Club Downtown", Api.Str(ld, "name"), "JSON-LD name");
            Check.Equal("100 Example Street", Api.Str(ld.GetProperty("address"), "streetAddress"), "JSON-LD street");
            Check.Equal(1, ld.GetProperty("aggregateRating").GetProperty("reviewCount").GetInt32(), "JSON-LD rating count");

            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/venues/" + id, reviewer.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(Api.Str(venue, "publicUrl"), Api.Str(read, "publicUrl"), "Member payload publicUrl");

            await Api.ExpectAsync(f, HttpMethod.Post, DirectoryTestKit.AdminVenues + "/" + id + "/close", HttpFixture.AdminToken, new { reason = "Moved away" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await NotFoundAsync(f, "venues", slug, "Closed venues have no page", ct).ConfigureAwait(false);
            JsonElement closed = await Api.ExpectAsync(f, HttpMethod.Get, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(closed.GetProperty("publicUrl").ValueKind == JsonValueKind.Null, "No publicUrl once closed");
            await NotFoundAsync(f, "venues", "no-such-venue", "Unknown slug", ct).ConfigureAwait(false);
        }

        private static async Task AddressVisibilityAsync(HttpFixture f, CancellationToken ct)
        {
            object Address() => new { line1 = "200 Hidden Lane", locality = "Exampleville", region = "EX", postalCode = "99999", countryCode = "US" };
            JsonElement approx = await DirectoryTestKit.VenueAsync(f, "Approximate Club", ct, 40.712776, -74.005974, new { address = Address(), addressVisibility = "Approximate" }).ConfigureAwait(false);
            JsonElement locality = await DirectoryTestKit.VenueAsync(f, "Locality Club", ct, 40.712776, -74.005974, new { address = Address(), addressVisibility = "LocalityOnly" }).ConfigureAwait(false);
            JsonElement exact = await DirectoryTestKit.VenueAsync(f, "Exact Club", ct, 40.712776, -74.005974, new { address = Address() }).ConfigureAwait(false);

            string a = await PageAsync(f, "venues", Api.Str(approx, "slug"), ct).ConfigureAwait(false);
            Check.True(!a.Contains("200 Hidden Lane", StringComparison.Ordinal) && !a.Contains("99999", StringComparison.Ordinal), "Approximate hides the street and postal code");
            Check.True(a.Contains("Exampleville", StringComparison.Ordinal), "Approximate shows the locality");
            JsonElement ald = JsonLd(a);
            Check.Equal(40.71, ald.GetProperty("geo").GetProperty("latitude").GetDouble(), "Approximate coordinates are rounded");
            Check.True(!ald.GetProperty("address").TryGetProperty("streetAddress", out JsonElement _), "No street in JSON-LD");

            string l = await PageAsync(f, "venues", Api.Str(locality, "slug"), ct).ConfigureAwait(false);
            Check.True(!l.Contains("200 Hidden Lane", StringComparison.Ordinal), "LocalityOnly hides the street");
            Check.True(!JsonLd(l).TryGetProperty("geo", out JsonElement _), "LocalityOnly has no coordinates");
            Check.True(!l.Contains("40.71", StringComparison.Ordinal), "No coordinates anywhere on a LocalityOnly page");

            string e = await PageAsync(f, "venues", Api.Str(exact, "slug"), ct).ConfigureAwait(false);
            Check.True(e.Contains("200 Hidden Lane", StringComparison.Ordinal) && e.Contains("99999", StringComparison.Ordinal), "Exact shows the full address");
            Check.True(Math.Abs(JsonLd(e).GetProperty("geo").GetProperty("latitude").GetDouble() - 40.71278) < 0.000001, "Exact coordinates (5 decimals, as members see them)");
        }

        private static async Task SlugsAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            JsonElement venue = await DirectoryTestKit.VenueAsync(f, "Slug Club", ct, extra: new { address = new { locality = "Exampleville", region = "EX", countryCode = "US" } }).ConfigureAwait(false);
            string id = Api.Str(venue, "id");
            Check.Equal("slug-club-exampleville-ex", Api.Str(venue, "slug"), "Derived");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken, new { slug = "bad--slug" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken, new { slug = "ab" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken, new { slug = "search" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement other = await DirectoryTestKit.VenueAsync(f, "Other Club", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken, new { slug = Api.Str(other, "slug") }, HttpStatusCode.Conflict, "SlugInUse", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + id, HttpFixture.AdminToken, new { slug = "slug-club-renamed" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult moved = await GetAsync(f, _Resolve + "venues/slug-club-exampleville-ex", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.MovedPermanently, moved.Status, "Old venue slug is 301");
            Check.Equal("/venues/slug-club-renamed", moved.Headers["Location"], "Redirect to the current slug");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, DirectoryTestKit.AdminVenues + "/" + Api.Str(other, "id"), HttpFixture.AdminToken, new { slug = "slug-club-exampleville-ex" }, HttpStatusCode.Conflict, "SlugInUse", ct).ConfigureAwait(false);

            JsonElement check = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/public-pages/slugs/check", admin.AccessToken, new { kind = "Venue", slug = "slug-club-renamed" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("taken", Api.Str(check, "problem"), "Taken for another object");
            check = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/public-pages/slugs/check", admin.AccessToken, new { kind = "Venue", slug = "slug-club-renamed", id }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(check.GetProperty("available").GetBoolean(), "Own slug is available");
            Check.True(Api.Str(check, "url").EndsWith("/venues/slug-club-renamed", StringComparison.Ordinal), "Check returns the URL");
            check = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/public-pages/slugs/check", admin.AccessToken, new { kind = "Event", slug = "Bad Slug" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("format", Api.Str(check, "problem"), "Format problem");
            check = await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/public-pages/slugs/check", admin.AccessToken, new { kind = "Profile", slug = "slug-club-renamed" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(check.GetProperty("available").GetBoolean(), "Unique per kind: a profile may reuse a venue slug");

            JsonElement created = await EventTestKit.CreateAsync(f, admin.AccessToken, EventTestKit.Body("Slug Social", EventTestKit.Local(9, 18), EventTestKit.Local(9, 20), new { audience = "Public" }), ct).ConfigureAwait(false);
            string eventId = Api.Str(created, "id");
            Check.Equal("slug-social-exampleville-ex", Api.Str(created, "slug"), "Event slug from title, locality, region");
            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + eventId + "/publish", admin.AccessToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + eventId, admin.AccessToken, new { slug = "x" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + eventId, admin.AccessToken, new { slug = "slug-social-renamed" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult eventMoved = await GetAsync(f, _Resolve + "events/slug-social-exampleville-ex", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.MovedPermanently, eventMoved.Status, "Old event slug is 301");
            Check.Equal("/events/slug-social-renamed", eventMoved.Headers["Location"], "Event redirect");
            JsonElement byOld = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/slug-social-exampleville-ex", admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("slug-social-renamed", Api.Str(byOld, "slug"), "The API finds an event by a previous slug");
            JsonElement second = await EventTestKit.CreateAsync(f, admin.AccessToken, EventTestKit.Body("Slug Social", EventTestKit.Local(9, 18), EventTestKit.Local(9, 20)), ct).ConfigureAwait(false);
            Check.True(Api.Str(second, "slug") != "slug-social-exampleville-ex", "A previous slug is not handed to a new event");
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + Api.Str(second, "id"), admin.AccessToken, new { slug = "slug-social-renamed" }, HttpStatusCode.Conflict, "SlugInUse", ct).ConfigureAwait(false);

            TestMember member = await SafetyHelpers.MemberAsync(f, ct, "Slug Person").ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/public-page", member.AccessToken, new { enabled = true, slug = "slug-person-one" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/public-page", member.AccessToken, new { slug = "slug-person-two" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            ApiResult profileMoved = await GetAsync(f, _Resolve + "profiles/slug-person-one", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.MovedPermanently, profileMoved.Status, "Old profile slug is 301");
            Check.Equal("/profiles/slug-person-two", profileMoved.Headers["Location"], "Profile redirect");
            ApiResult upper = await GetAsync(f, _Resolve + "profiles/Slug-Person-Two", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.MovedPermanently, upper.Status, "Case variants redirect to the canonical slug");
            TestMember another = await SafetyHelpers.MemberAsync(f, ct, "Another Person").ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/public-page", another.AccessToken, new { enabled = true, slug = "slug-person-two" }, HttpStatusCode.Conflict, "SlugInUse", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/public-page", another.AccessToken, new { enabled = true, slug = "slug-person-one" }, HttpStatusCode.Conflict, "SlugInUse", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/me/public-page", another.AccessToken, new { slug = "new" }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
        }

        private static async Task EventPagesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            TestMember host = await SafetyHelpers.MemberAsync(f, ct, "Event Host").ConfigureAwait(false);
            string publicId = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Open Session", ct, new { audience = "Public", hostUserIds = new[] { host.Id } }).ConfigureAwait(false);
            string membersId = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Members Session", ct, new { audience = "Members" }).ConfigureAwait(false);
            JsonElement draft = await EventTestKit.CreateAsync(f, admin.AccessToken, EventTestKit.Body("Draft Session", EventTestKit.Local(11, 18), EventTestKit.Local(11, 20), new { audience = "Public" }), ct).ConfigureAwait(false);

            JsonElement read = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + publicId, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string slug = Api.Str(read, "slug");
            Check.True(Api.Str(read, "publicUrl").EndsWith("/events/" + slug, StringComparison.Ordinal), "Public events carry publicUrl");
            JsonElement members = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/events/" + membersId, admin.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(members.GetProperty("publicUrl").ValueKind == JsonValueKind.Null, "Members events have no publicUrl");

            string html = await PageAsync(f, "events", slug, ct).ConfigureAwait(false);
            JsonElement ld = JsonLd(html);
            Check.Equal("Event", Api.Str(ld, "@type"), "Event JSON-LD");
            Check.Equal("https://schema.org/EventScheduled", Api.Str(ld, "eventStatus"), "Scheduled");
            Check.Equal("https://schema.org/OfflineEventAttendanceMode", Api.Str(ld, "eventAttendanceMode"), "In person");
            Check.Equal("Place", Api.Str(ld.GetProperty("location"), "@type"), "Location");
            Check.True(Api.Str(ld, "startDate").Contains('T', StringComparison.Ordinal) && Api.Str(ld, "startDate").Length >= 25, "ISO start with an offset");
            Check.True(html.Contains("Open Session", StringComparison.Ordinal) && html.Contains("Exampleville", StringComparison.Ordinal), "Title and place");
            Check.True(html.Contains("/app/events/" + publicId, StringComparison.Ordinal), "App link");

            await NotFoundAsync(f, "events", Api.Str(members, "slug"), "Members events have no page", ct).ConfigureAwait(false);
            await NotFoundAsync(f, "events", Api.Str(draft, "slug"), "Drafts have no page", ct).ConfigureAwait(false);

            await Api.ExpectErrorAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + publicId, host.AccessToken, new { title = "Renamed" }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement renamed = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/events/" + publicId, host.AccessToken, new { slug = "open-session-hosted" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("open-session-hosted", Api.Str(renamed, "slug"), "Hosts may change the slug");

            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/events/" + publicId + "/cancel", admin.AccessToken, new { reason = "The hall is closed." }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string canceled = await PageAsync(f, "events", "open-session-hosted", ct).ConfigureAwait(false);
            Check.Equal("https://schema.org/EventCancelled", Api.Str(JsonLd(canceled), "eventStatus"), "Canceled events keep a page with EventCancelled");
            Check.True(canceled.Contains("This event was canceled.", StringComparison.Ordinal), "Canceled banner");
        }

        private static async Task ProfilePagesAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await SafetyHelpers.MemberAsync(f, ct, "Sam Example").ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/profile", member.AccessToken, new
            {
                bio = "Trains <b>every</b> day.",
                profile = new { affiliation = "Example Network", city = "Secretville", region = "EX", goals = new { keys = new[] { "Teaching" } } }
            }, HttpStatusCode.OK, ct).ConfigureAwait(false);

            JsonElement settings = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/me/public-page", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!settings.GetProperty("enabled").GetBoolean(), "Off by default");
            Check.True(settings.GetProperty("url").ValueKind == JsonValueKind.Null, "No URL while off");
            string suggested = Api.Str(settings, "suggestedSlug");
            Check.Equal("sam-example-ex", suggested, "Suggested slug uses only public values (the members-only city stays out)");
            JsonElement me = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/auth/me", member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(!me.GetProperty("publicPage").GetProperty("enabled").GetBoolean(), "MeResponse.publicPage off");
            await NotFoundAsync(f, "profiles", suggested, "No page before opting in", ct).ConfigureAwait(false);

            JsonElement on = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/public-page", member.AccessToken, new { enabled = true }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(suggested, Api.Str(on, "slug"), "Turning on without a slug uses the suggestion");
            Check.True(Api.Str(on, "url").EndsWith("/profiles/" + suggested, StringComparison.Ordinal), "URL");
            string html = await PageAsync(f, "profiles", suggested, ct).ConfigureAwait(false);
            Check.True(html.Contains("Sam Example", StringComparison.Ordinal), "Display name");
            Check.True(html.Contains("Example Network", StringComparison.Ordinal), "Affiliation");
            Check.True(html.Contains("Trains &lt;b&gt;every&lt;/b&gt; day.", StringComparison.Ordinal), "Bio is encoded");
            Check.True(!html.Contains("Secretville", StringComparison.Ordinal), "Members-only fields stay off");
            Check.True(!html.Contains("Teaching", StringComparison.Ordinal), "Private fields stay off");
            Check.True(!html.Contains(member.Email, StringComparison.Ordinal), "No email");
            JsonElement ld = JsonLd(html);
            Check.Equal("ProfilePage", Api.Str(ld, "@type"), "ProfilePage JSON-LD");
            Check.Equal("Person", Api.Str(ld.GetProperty("mainEntity"), "@type"), "Person");
            Check.Equal("profile", Meta(html, "og:type"), "og:type profile");
            JsonElement profile = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/users/" + member.Id, member.AccessToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(Api.Str(profile, "publicUrl").EndsWith("/profiles/" + suggested, StringComparison.Ordinal), "Profile payload publicUrl");

            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/public-page", member.AccessToken, new { enabled = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await NotFoundAsync(f, "profiles", suggested, "Opted out members have no page", ct).ConfigureAwait(false);
            JsonElement admin = await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/admin/members/" + member.Id + "/public-page", HttpFixture.AdminToken, new { enabled = true, slug = "sam-by-admin" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(admin.GetProperty("enabled").GetBoolean(), "Admins may set the page");
            JsonElement adminRead = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/admin/members/" + member.Id + "/public-page", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("sam-by-admin", Api.Str(adminRead, "slug"), "Admin read");
            await PageAsync(f, "profiles", "sam-by-admin", ct).ConfigureAwait(false);
            Check.True(await ModerationTestKit.AuditCountAsync(f, "profile.public_page", member.Id, ct).ConfigureAwait(false) >= 2, "Audited");

            await Api.ExpectAsync(f, HttpMethod.Post, "/api/v1/admin/members/" + member.Id + "/ban", HttpFixture.AdminToken, new { reason = "Spam" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await NotFoundAsync(f, "profiles", "sam-by-admin", "Banned members have no page", ct).ConfigureAwait(false);
            Check.True(!(await GetAsync(f, "/api/v1/public/pages/sitemaps/profiles-1.xml", ct).ConfigureAwait(false)).Text.Contains("sam-by-admin", StringComparison.Ordinal), "Banned members leave the sitemap");

            TestMember leaving = await SafetyHelpers.MemberAsync(f, ct, "Leaving Member").ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/public-page", leaving.AccessToken, new { enabled = true, slug = "leaving-member" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await PageAsync(f, "profiles", "leaving-member", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Delete, "/api/v1/me", leaving.AccessToken, new { confirm = "DELETE" }, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
            await NotFoundAsync(f, "profiles", "leaving-member", "Deleted members have no page", ct).ConfigureAwait(false);
        }

        private static async Task EscapingAsync(HttpFixture f, CancellationToken ct)
        {
            JsonElement venue = await DirectoryTestKit.VenueAsync(f, "Club </script><script>alert(1)</script> \"Quote\" & Co", ct, extra: new
            {
                summary = "Summary with <img src=x onerror=alert(1)> tag",
                description = "Line one\n</script>"
            }).ConfigureAwait(false);
            string html = await PageAsync(f, "venues", Api.Str(venue, "slug"), ct).ConfigureAwait(false);
            Check.True(!html.Contains("<script>alert(1)", StringComparison.Ordinal), "No injected script element");
            Check.True(!html.Contains("<img src=x", StringComparison.Ordinal), "No injected image element");
            Check.True(html.Contains("&lt;/script&gt;&lt;script&gt;alert(1)&lt;/script&gt; &quot;Quote&quot; &amp; Co", StringComparison.Ordinal), "Text is HTML-encoded");
            Check.Equal(3, Regex.Matches(html, "</script>", RegexOptions.CultureInvariant).Count, "Only the static theme, gallery player and JSON-LD scripts close elements");
            JsonElement ld = JsonLd(html);
            Check.True(Api.Str(ld, "name").Contains("</script>", StringComparison.Ordinal), "JSON-LD keeps the value after decoding");
            Check.True(html.Contains("\\u003c/script\\u003e", StringComparison.OrdinalIgnoreCase), "JSON-LD escapes angle brackets");
        }

        private static async Task SitemapAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember admin = await Api.AdminAsync(f, ct).ConfigureAwait(false);
            JsonElement venue = await DirectoryTestKit.VenueAsync(f, "Map Club", ct).ConfigureAwait(false);
            string upcoming = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Sitemap Upcoming", ct, new { audience = "Public" }).ConfigureAwait(false);
            string membersOnly = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Sitemap Members", ct, new { audience = "Members" }).ConfigureAwait(false);
            string ended = await EventTestKit.PublishedAsync(f, admin.AccessToken, "Sitemap Ended", ct, new { audience = "Public" }).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE events SET startutc = now() - interval '3 days', endutc = now() - interval '2 days' WHERE id = '" + ended + "';", false, ct).ConfigureAwait(false);
            TestMember member = await SafetyHelpers.MemberAsync(f, ct, "Map Person").ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/public-page", member.AccessToken, new { enabled = true, slug = "map-person" }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            TestMember hidden = await SafetyHelpers.MemberAsync(f, ct, "Hidden Person").ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/me/public-page", hidden.AccessToken, new { enabled = false, slug = "hidden-person" }, HttpStatusCode.OK, ct).ConfigureAwait(false);

            ApiResult index = await GetAsync(f, "/api/v1/public/pages/sitemap.xml", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, index.Status, "Index");
            Check.True((index.ContentType ?? String.Empty).StartsWith("application/xml", StringComparison.Ordinal), "XML");
            Check.True(index.Text.Contains("<sitemapindex", StringComparison.Ordinal), "Sitemap index");
            Check.True(index.Text.Contains("/sitemap-pages.xml</loc>", StringComparison.Ordinal), "Website marketing pages listed");
            foreach (string kind in new[] { "venues", "events", "profiles" }) Check.True(index.Text.Contains("/sitemap-" + kind + "-1.xml</loc>", StringComparison.Ordinal), "Child for " + kind);

            string venues = (await GetAsync(f, "/api/v1/public/pages/sitemaps/venues-1.xml", ct).ConfigureAwait(false)).Text;
            Check.True(venues.Contains("/venues/" + Api.Str(venue, "slug") + "</loc>", StringComparison.Ordinal) && venues.Contains("<lastmod>", StringComparison.Ordinal), "Venue listed with lastmod");
            string events = (await GetAsync(f, "/api/v1/public/pages/sitemaps/events-1.xml", ct).ConfigureAwait(false)).Text;
            Check.True(events.Contains("/events/" + (await EventTestKit.RecordAsync(f, upcoming, ct).ConfigureAwait(false)).Slug + "</loc>", StringComparison.Ordinal), "Upcoming Public event listed");
            Check.True(!events.Contains((await EventTestKit.RecordAsync(f, membersOnly, ct).ConfigureAwait(false)).Slug, StringComparison.Ordinal), "Members events never listed");
            string endedSlug = (await EventTestKit.RecordAsync(f, ended, ct).ConfigureAwait(false)).Slug;
            Check.True(!events.Contains(endedSlug, StringComparison.Ordinal), "Ended events leave the sitemap");
            await PageAsync(f, "events", endedSlug, ct).ConfigureAwait(false);
            string profiles = (await GetAsync(f, "/api/v1/public/pages/sitemaps/profiles-1.xml", ct).ConfigureAwait(false)).Text;
            Check.True(profiles.Contains("/profiles/map-person</loc>", StringComparison.Ordinal) && !profiles.Contains("hidden-person", StringComparison.Ordinal), "Only opted-in profiles");
            Check.Equal(HttpStatusCode.NotFound, (await GetAsync(f, "/api/v1/public/pages/sitemaps/venues-2.xml", ct).ConfigureAwait(false)).Status, "Pages past the end are 404");
            Check.Equal(HttpStatusCode.NotFound, (await GetAsync(f, "/api/v1/public/pages/sitemaps/gyms-1.xml", ct).ConfigureAwait(false)).Status, "Unknown names are 404");

            await DirectoryTestKit.VenueAsync(f, "Map Club Two", ct).ConfigureAwait(false);
            await DirectoryTestKit.VenueAsync(f, "Map Club Three", ct).ConfigureAwait(false);
            IPublicPageMethods pages = f.Database.PublicPages;
            Check.Equal(3L, await pages.CountSitemapAsync(PublicPageKindEnum.Venue, DateTime.UtcNow, ct).ConfigureAwait(false), "Count");
            List<PublicPageEntry> first = await pages.SitemapPageAsync(PublicPageKindEnum.Venue, DateTime.UtcNow, 0, 2, ct).ConfigureAwait(false);
            List<PublicPageEntry> rest = await pages.SitemapPageAsync(PublicPageKindEnum.Venue, DateTime.UtcNow, 2, 2, ct).ConfigureAwait(false);
            Check.Equal(2, first.Count, "Page size bounds the query");
            Check.Equal(1, rest.Count, "Second page");
            Check.True(!first.Select(e => e.Slug).Intersect(rest.Select(e => e.Slug)).Any(), "Stable order without overlap");
            bool threw = false;
            try { await pages.SitemapPageAsync(PublicPageKindEnum.Venue, DateTime.UtcNow, 0, IPublicPageMethods.MaxSitemapPage + 1, ct).ConfigureAwait(false); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            Check.True(threw, "More than 50,000 per page is refused");
            f.Services.PublicPages.SitemapPageSize = 1;
            Check.Equal(100, f.Services.PublicPages.SitemapPageSize, "Page size has a floor of 100");
        }

        private static async Task RobotsAsync(HttpFixture f, CancellationToken ct)
        {
            ApiResult robots = await GetAsync(f, "/api/v1/public/pages/robots.txt", ct).ConfigureAwait(false);
            Check.Equal(HttpStatusCode.OK, robots.Status, "robots.txt");
            foreach (string line in new[] { "User-agent: *", "Disallow: /app", "Disallow: /admin", "Disallow: /api", "Allow: /api/v1/media/files/public/", "Sitemap: " + f.Services.Property.PublicBaseUrl + "/sitemap.xml" })
                Check.True(robots.Text.Contains(line + "\n", StringComparison.Ordinal), "robots.txt has " + line);
            foreach (string path in new[] { "lessons/first-steps", "threads/thr_1", "app/feed" })
            {
                ApiResult pass = await GetAsync(f, _Resolve + path, ct).ConfigureAwait(false);
                Check.Equal(HttpStatusCode.NotFound, pass.Status, path + " is not a page");
                Check.Equal("/spa.html", pass.Headers.TryGetValue("X-Accel-Redirect", out string? accel) ? accel : null, path + " passes through to the website shell");
            }
        }

        private static async Task CustomPathsAsync(HttpFixture f, CancellationToken ct)
        {
            JsonElement venue = await DirectoryTestKit.VenueAsync(f, "Custom Club", ct).ConfigureAwait(false);
            string slug = Api.Str(venue, "slug");
            Check.True(Api.Str(venue, "publicUrl").EndsWith("/places/" + slug, StringComparison.Ordinal), "publicUrl uses the property's path word");
            await PageAsync(f, "places", slug, ct).ConfigureAwait(false);
            ApiResult engine = await GetAsync(f, _Resolve + "venues/" + slug, ct).ConfigureAwait(false);
            Check.True(engine.Headers.ContainsKey("X-Accel-Redirect"), "The engine default word is no longer a page path");
            JsonElement property = await Api.ExpectAsync(f, HttpMethod.Get, "/api/v1/property", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("places", Api.Str(property.GetProperty("publicPages"), "venuesPath"), "GET /api/v1/property publicPages");
            Check.Equal("people", Api.Str(property.GetProperty("publicPages"), "profilesPath"), "profilesPath");
            string index = (await GetAsync(f, "/api/v1/public/pages/sitemaps/venues-1.xml", ct).ConfigureAwait(false)).Text;
            Check.True(index.Contains("/places/" + slug + "</loc>", StringComparison.Ordinal), "Sitemap uses the path word");
        }

        private static async Task DisabledAsync(HttpFixture f, CancellationToken ct)
        {
            JsonElement venue = await DirectoryTestKit.VenueAsync(f, "Quiet Club", ct).ConfigureAwait(false);
            Check.True(venue.GetProperty("publicUrl").ValueKind == JsonValueKind.Null, "No publicUrl");
            ApiResult page = await GetAsync(f, _Resolve + "venues/" + Api.Str(venue, "slug"), ct).ConfigureAwait(false);
            Check.True(page.Headers.ContainsKey("X-Accel-Redirect"), "Pages off: every path passes through");
            string index = (await GetAsync(f, "/api/v1/public/pages/sitemap.xml", ct).ConfigureAwait(false)).Text;
            Check.True(index.Contains("sitemap-pages.xml", StringComparison.Ordinal) && !index.Contains("sitemap-venues", StringComparison.Ordinal), "Only the website pages");
            TestMember member = await SafetyHelpers.MemberAsync(f, ct, "Quiet Person").ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, "/api/v1/me/public-page", member.AccessToken, null, HttpStatusCode.NotFound, "NotAvailable", ct).ConfigureAwait(false);
        }

        #endregion
    }
}
