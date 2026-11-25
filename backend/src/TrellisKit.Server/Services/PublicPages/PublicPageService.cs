namespace TrellisKit.Server.Services.PublicPages
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Security;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Routes;
    using TrellisKit.Server.Settings;

    /// <summary>
    /// Public landing pages of one property (docs/PHASE4_CONTRACT.md section 18): which venues, events, and member profiles
    /// have a page, their URLs for payloads and share actions, the member's opt-in profile page settings, slug checks, and
    /// the server-rendered pages, sitemap, and robots.txt that crawlers read.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public partial class PublicPageService
    {
        #region Public-Members

        /// <summary>
        /// URLs per child sitemap file (the protocol allows 50,000). Default 10000. Minimum 100, maximum 50000.
        /// </summary>
        public int SitemapPageSize
        {
            get { return _SitemapPageSize; }
            set { _SitemapPageSize = Math.Clamp(value, 100, IPublicPageMethods.MaxSitemapPage); }
        }

        /// <summary>
        /// Most child sitemap files listed per kind (bounds the index). Default 100. Minimum 1, maximum 1000.
        /// </summary>
        public int MaxSitemapFilesPerKind
        {
            get { return _MaxSitemapFilesPerKind; }
            set { _MaxSitemapFilesPerKind = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Most instructors listed on a venue page. Default 20. Minimum 1, maximum 100.
        /// </summary>
        public int MaxInstructors
        {
            get { return _MaxInstructors; }
            set { _MaxInstructors = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// The website file served for paths that are not public pages (X-Accel-Redirect).
        /// </summary>
        public const string SpaShell = "/spa.html";

        /// <summary>
        /// The static sitemap of the website's marketing pages (written by the website build).
        /// </summary>
        public const string PagesSitemap = "/sitemap-pages.xml";

        /// <summary>
        /// The fixed (engine) names of the child sitemaps of each kind.
        /// </summary>
        public static readonly IReadOnlyDictionary<PublicPageKindEnum, string> SitemapNames = new Dictionary<PublicPageKindEnum, string>
        {
            { PublicPageKindEnum.Venue, "venues" },
            { PublicPageKindEnum.Event, "events" },
            { PublicPageKindEnum.Profile, "profiles" }
        };

        #endregion

        #region Private-Members

        private const string _XmlType = "application/xml; charset=utf-8";
        private const string _SitemapCache = "public, max-age=3600";
        private readonly ApplicationServices _Services;
        private int _SitemapPageSize = 10000;
        private int _MaxSitemapFilesPerKind = 100;
        private int _MaxInstructors = 20;
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions { WriteIndented = false };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public PublicPageService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The property's public pages configuration (engine defaults when absent).
        /// </summary>
        /// <returns>Configuration.</returns>
        public PropertyPublicPages Config()
        {
            return _Services.Property.Property.EffectivePublicPages;
        }

        /// <summary>
        /// Site-relative path of a page.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="slug">Slug.</param>
        /// <returns>Path, or null without a slug.</returns>
        public string? PathOf(PublicPageKindEnum kind, string? slug)
        {
            if (String.IsNullOrWhiteSpace(slug)) return null;
            return "/" + Config().PathOf(kind) + "/" + Uri.EscapeDataString(slug.Trim());
        }

        /// <summary>
        /// Absolute URL of a page on the website origin (web.publicBaseUrl).
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="slug">Slug.</param>
        /// <returns>URL, or null without a slug.</returns>
        public string? UrlOf(PublicPageKindEnum kind, string? slug)
        {
            string? path = PathOf(kind, slug);
            return path == null ? null : _Services.Property.PublicBaseUrl + path;
        }

        /// <summary>
        /// Whether a venue has a public page: pages on, the directory on, and the venue live and Active (any audience; the
        /// page shows business information only).
        /// </summary>
        /// <param name="venue">Venue.</param>
        /// <returns>True when it has a page.</returns>
        public bool VenueHasPage(Venue? venue)
        {
            return venue != null && Config().Enabled && _Services.Property.Property.Features.Directory && venue.DeletedUtc == null && venue.Status == VenueStatusEnum.Active &&
                venue.ModerationState == ModerationStateEnum.None && !String.IsNullOrWhiteSpace(venue.Slug);
        }

        /// <summary>
        /// Whether an event has a public page: pages on, events on, and the event Public, published or canceled, and not hidden.
        /// </summary>
        /// <param name="e">Event.</param>
        /// <returns>True when it has a page.</returns>
        public bool EventHasPage(EventRecord? e)
        {
            return e != null && Config().Enabled && _Services.Property.Property.Features.Events && e.DeletedUtc == null && e.Audience == ContentAudienceEnum.Public &&
                e.ModerationState == ModerationStateEnum.None && (e.Status == EventStatusEnum.Published || e.Status == EventStatusEnum.Canceled) && !String.IsNullOrWhiteSpace(e.Slug);
        }

        /// <summary>
        /// The public page URL of a venue, or null.
        /// </summary>
        /// <param name="venue">Venue.</param>
        /// <returns>URL, or null.</returns>
        public string? VenueUrl(Venue? venue)
        {
            return VenueHasPage(venue) ? UrlOf(PublicPageKindEnum.Venue, venue!.Slug) : null;
        }

        /// <summary>
        /// The public page URL of an event, or null.
        /// </summary>
        /// <param name="e">Event.</param>
        /// <returns>URL, or null.</returns>
        public string? EventUrl(EventRecord? e)
        {
            return EventHasPage(e) ? UrlOf(PublicPageKindEnum.Event, e!.Slug) : null;
        }

        /// <summary>
        /// The public profile page URL of a member, or null when the member has none (off, no slug, banned, or deleted).
        /// </summary>
        /// <param name="user">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>URL, or null.</returns>
        public async Task<string?> ProfileUrlAsync(User? user, CancellationToken token = default)
        {
            if (user == null || user.IsDeleted || user.IsBanned || !Config().Enabled) return null;
            ProfilePage? page = await _Services.Database.PublicPages.ReadProfileAsync(user.Id, token).ConfigureAwait(false);
            return page != null && page.Enabled && !String.IsNullOrWhiteSpace(page.Slug) ? UrlOf(PublicPageKindEnum.Profile, page.Slug) : null;
        }

        /// <summary>
        /// MeResponse.publicPage: the member's page settings without the suggestion.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<ProfilePageView> MeViewAsync(string userId, CancellationToken token = default)
        {
            ProfilePage? page = await _Services.Database.PublicPages.ReadProfileAsync(userId, token).ConfigureAwait(false);
            return View(page, null);
        }

        /// <summary>
        /// GET /api/v1/me/public-page and GET /api/v1/admin/members/{id}/public-page.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View with a suggested slug.</returns>
        /// <exception cref="ApiErrorException">404 NotFound (unknown or deleted member, or pages off).</exception>
        public async Task<ProfilePageView> GetProfilePageAsync(string userId, CancellationToken token = default)
        {
            RequireEnabled();
            User user = await RequireUserAsync(userId, token).ConfigureAwait(false);
            ProfilePage? page = await _Services.Database.PublicPages.ReadProfileAsync(user.Id, token).ConfigureAwait(false);
            return View(page, await SuggestSlugAsync(user, token).ConfigureAwait(false));
        }

        /// <summary>
        /// PUT /api/v1/me/public-page and PUT /api/v1/admin/members/{id}/public-page: turn the page on or off and set the
        /// slug (a changed slug keeps the previous one as a redirect). Turning the page on without any slug uses the
        /// suggested one. Audited as profile.public_page.
        /// </summary>
        /// <param name="info">Request (audit).</param>
        /// <param name="userId">Member.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed (slug), 404 NotFound, 409 SlugInUse.</exception>
        public async Task<ProfilePageView> UpdateProfilePageAsync(RequestInfo info, string userId, ProfilePageRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            RequireEnabled();
            User user = await RequireUserAsync(userId, token).ConfigureAwait(false);
            ProfilePage? existing = await _Services.Database.PublicPages.ReadProfileAsync(user.Id, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            ProfilePage page = new ProfilePage
            {
                UserId = user.Id,
                Enabled = request.Enabled ?? existing?.Enabled ?? false,
                Slug = existing?.Slug,
                CreatedUtc = existing?.CreatedUtc ?? now,
                UpdatedUtc = now
            };
            string? requested = String.IsNullOrWhiteSpace(request.Slug) ? null : request.Slug.Trim().ToLowerInvariant();
            if (requested != null && !String.Equals(requested, existing?.Slug, StringComparison.OrdinalIgnoreCase))
            {
                if (!PublicSlugRules.IsValid(requested)) throw ApiErrorException.Validation(new Dictionary<string, string> { { "slug", PublicSlugRules.Message } });
                if (await _Services.Database.PublicPages.SlugTakenAsync(PublicPageKindEnum.Profile, requested, user.Id, token).ConfigureAwait(false)) throw SlugInUse();
                page.Slug = requested;
            }
            if (page.Enabled && String.IsNullOrWhiteSpace(page.Slug)) page.Slug = await SuggestSlugAsync(user, token).ConfigureAwait(false);
            try
            {
                await _Services.Database.PublicPages.SaveProfileAsync(page, existing?.Slug, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw SlugInUse();
            }
            if (!String.Equals(page.Slug, existing?.Slug, StringComparison.Ordinal)) TrellisKitTelemetry.Increment(PublicPageTelemetry.SlugChanges, "kind", PublicPageKindEnum.Profile.ToString());
            await _Services.Audit.WriteAsync(info, "profile.public_page", TargetTypeEnum.User.ToString(), user.Id, (page.Enabled ? "On" : "Off") + (page.Slug == null ? String.Empty : ": " + page.Slug), token).ConfigureAwait(false);
            return View(page, await SuggestSlugAsync(user, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/public-pages/slugs/check: whether a slug is valid and free for an object.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<SlugCheckView> CheckSlugAsync(ContentActor actor, SlugCheckRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            _Services.Limits.EnforceMember(actor, "public-page-slugs", _Services.Settings.RateLimits.MemberReadsPerMinute, RateLimitService.Minute);
            string slug = (request.Slug ?? String.Empty).Trim().ToLowerInvariant();
            string? problem = PublicSlugRules.Problem(slug);
            SlugCheckView view = new SlugCheckView { Kind = request.Kind, Slug = slug, Valid = problem == null, Path = PathOf(request.Kind, slug), Url = UrlOf(request.Kind, slug) };
            if (problem == null)
            {
                string? except = request.Kind == PublicPageKindEnum.Profile ? (String.IsNullOrWhiteSpace(request.Id) || !actor.IsStaff ? actor.UserId : request.Id) : request.Id;
                if (await _Services.Database.PublicPages.SlugTakenAsync(request.Kind, slug, except, token).ConfigureAwait(false)) problem = "taken";
            }
            view.Problem = problem;
            view.Available = problem == null;
            return view;
        }

        /// <summary>
        /// GET /api/v1/public/pages/resolve/{path}/{slug}: render the page at /{path}/{slug}. A path that is not one of
        /// the property's public page paths answers with X-Accel-Redirect to the website shell, so the website proxy
        /// serves its own route.
        /// </summary>
        /// <param name="path">First path segment.</param>
        /// <param name="slug">Slug.</param>
        /// <param name="sourceIp">Caller address.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ApiErrorException">429 RateLimited.</exception>
        public async Task<PublicPageResult> ResolveAsync(string? path, string? slug, string? sourceIp, CancellationToken token = default)
        {
            PropertyPublicPages config = Config();
            PublicPageKindEnum? kind = config.Enabled ? config.KindOf(path) : null;
            if (kind == PublicPageKindEnum.Venue && !_Services.Property.Property.Features.Directory) kind = null;
            if (kind == PublicPageKindEnum.Event && !_Services.Property.Property.Features.Events) kind = null;
            if (kind == null)
            {
                TrellisKitTelemetry.Increment(PublicPageTelemetry.Requests, "kind", "None", "outcome", "PassThrough");
                // The proxy keeps this content type when it serves the shell, so it is the shell's (HTML).
                return new PublicPageResult { StatusCode = 404, Body = "<!DOCTYPE html><title>Not a public page path</title>", CacheControl = "no-cache", AccelRedirect = SpaShell };
            }
            _Services.Limits.EnforceIp(sourceIp, "public-pages", _Services.Settings.RateLimits.PublicPagesPerMinute, RateLimitService.Minute);
            string key = (slug ?? String.Empty).Trim();
            PublicPageResult result;
            if (key.Length == 0 || key.Length > 200) result = NotFound();
            else if (kind == PublicPageKindEnum.Venue) result = await VenuePageAsync(key, token).ConfigureAwait(false);
            else if (kind == PublicPageKindEnum.Event) result = await EventPageAsync(key, sourceIp, token).ConfigureAwait(false);
            else result = await ProfilePageAsync(key, token).ConfigureAwait(false);
            string outcome = result.Location != null ? "Redirect" : result.StatusCode == 200 ? "Ok" : "NotFound";
            TrellisKitTelemetry.Increment(PublicPageTelemetry.Requests, "kind", kind.Value.ToString(), "outcome", outcome);
            return result;
        }

        /// <summary>
        /// GET /sitemap.xml: a sitemap index of the website's marketing pages and one child sitemap per
        /// <see cref="SitemapPageSize"/> URLs of each kind (at most <see cref="MaxSitemapFilesPerKind"/> per kind).
        /// </summary>
        /// <param name="sourceIp">Caller address.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ApiErrorException">429 RateLimited.</exception>
        public async Task<PublicPageResult> SitemapIndexAsync(string? sourceIp, CancellationToken token = default)
        {
            _Services.Limits.EnforceIp(sourceIp, "public-pages", _Services.Settings.RateLimits.PublicPagesPerMinute, RateLimitService.Minute);
            string site = _Services.Property.PublicBaseUrl;
            DateTime now = DateTime.UtcNow;
            StringBuilder xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
            xml.Append("  <sitemap><loc>").Append(Xml(site + PagesSitemap)).Append("</loc></sitemap>\n");
            foreach (PublicPageKindEnum kind in Kinds())
            {
                long count = await _Services.Database.PublicPages.CountSitemapAsync(kind, now, token).ConfigureAwait(false);
                long files = Math.Min(MaxSitemapFilesPerKind, (count + SitemapPageSize - 1) / SitemapPageSize);
                for (long i = 1; i <= files; i++)
                    xml.Append("  <sitemap><loc>").Append(Xml(site + "/sitemap-" + SitemapNames[kind] + "-" + i.ToString(CultureInfo.InvariantCulture) + ".xml")).Append("</loc></sitemap>\n");
            }
            xml.Append("</sitemapindex>\n");
            TrellisKitTelemetry.Increment(PublicPageTelemetry.Requests, "kind", "Sitemap", "outcome", "Ok");
            return new PublicPageResult { ContentType = _XmlType, Body = xml.ToString(), CacheControl = _SitemapCache };
        }

        /// <summary>
        /// GET /sitemap-{venues|events|profiles}-{n}.xml: one child sitemap (urlset) with loc, lastmod, changefreq, and
        /// priority. Venues: live Active venues; events: Published Public events that have not ended (past events keep their
        /// page but leave the sitemap); profiles: enabled pages of members who are neither banned nor deleted.
        /// </summary>
        /// <param name="name">File name, for example venues-1.xml.</param>
        /// <param name="sourceIp">Caller address.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result (404 for an unknown name or page).</returns>
        /// <exception cref="ApiErrorException">429 RateLimited.</exception>
        public async Task<PublicPageResult> SitemapAsync(string? name, string? sourceIp, CancellationToken token = default)
        {
            _Services.Limits.EnforceIp(sourceIp, "public-pages", _Services.Settings.RateLimits.PublicPagesPerMinute, RateLimitService.Minute);
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(name ?? String.Empty, "^(venues|events|profiles)-([1-9][0-9]{0,3})\\.xml$");
            PublicPageKindEnum? kind = null;
            if (m.Success) kind = SitemapNames.First(p => p.Value == m.Groups[1].Value).Key;
            if (kind == null || !Kinds().Contains(kind.Value)) return SitemapNotFound();
            int number = Int32.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (number > MaxSitemapFilesPerKind) return SitemapNotFound();
            DateTime now = DateTime.UtcNow;
            List<PublicPageEntry> rows = await _Services.Database.PublicPages.SitemapPageAsync(kind.Value, now, (long)(number - 1) * SitemapPageSize, SitemapPageSize, token).ConfigureAwait(false);
            if (rows.Count == 0 && number > 1) return SitemapNotFound();
            string priority = kind == PublicPageKindEnum.Event ? "0.7" : kind == PublicPageKindEnum.Venue ? "0.6" : "0.5";
            string changefreq = kind == PublicPageKindEnum.Event ? "daily" : kind == PublicPageKindEnum.Venue ? "weekly" : "monthly";
            StringBuilder xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
            foreach (PublicPageEntry row in rows)
            {
                xml.Append("  <url><loc>").Append(Xml(UrlOf(kind.Value, row.Slug) ?? String.Empty)).Append("</loc><lastmod>")
                    .Append(row.LastModifiedUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append("</lastmod><changefreq>").Append(changefreq)
                    .Append("</changefreq><priority>").Append(priority).Append("</priority></url>\n");
            }
            xml.Append("</urlset>\n");
            TrellisKitTelemetry.Increment(PublicPageTelemetry.Requests, "kind", "Sitemap", "outcome", "Ok");
            return new PublicPageResult { ContentType = _XmlType, Body = xml.ToString(), CacheControl = _SitemapCache };
        }

        /// <summary>
        /// GET /robots.txt: allow the site, disallow the member app, the admin console, and the API (except the public media,
        /// avatar, and brand image paths the pages use), and point at the sitemap.
        /// </summary>
        /// <param name="sourceIp">Caller address.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ApiErrorException">429 RateLimited.</exception>
        public PublicPageResult Robots(string? sourceIp)
        {
            _Services.Limits.EnforceIp(sourceIp, "public-pages", _Services.Settings.RateLimits.PublicPagesPerMinute, RateLimitService.Minute);
            StringBuilder text = new StringBuilder();
            text.Append("User-agent: *\n");
            text.Append("Allow: /\n");
            text.Append("Allow: ").Append(Constants.ApiPrefix).Append("/media/files/public/\n");
            text.Append("Allow: ").Append(Constants.ApiPrefix).Append("/media/avatars/\n");
            text.Append("Allow: ").Append(PropertyRoutes.AssetPathPrefix).Append('\n');
            text.Append("Disallow: /app\n");
            text.Append("Disallow: /admin\n");
            text.Append("Disallow: /api\n");
            text.Append("\nSitemap: ").Append(_Services.Property.PublicBaseUrl).Append("/sitemap.xml\n");
            TrellisKitTelemetry.Increment(PublicPageTelemetry.Requests, "kind", "Robots", "outcome", "Ok");
            return new PublicPageResult { ContentType = "text/plain; charset=utf-8", Body = text.ToString(), CacheControl = _SitemapCache };
        }

        #endregion

        #region Private-Methods

        private void RequireEnabled()
        {
            if (!Config().Enabled) throw new ApiErrorException(404, "NotAvailable", "Public pages are turned off for this property.");
        }

        private async Task<User> RequireUserAsync(string userId, CancellationToken token)
        {
            User? user = String.IsNullOrWhiteSpace(userId) ? null : await _Services.Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) throw ApiErrorException.NotFound("Member");
            return user;
        }

        private static ApiErrorException SlugInUse()
        {
            return new ApiErrorException(409, "SlugInUse", "Another page uses this slug.", new Dictionary<string, string> { { "slug", "In use." } });
        }

        private ProfilePageView View(ProfilePage? page, string? suggested)
        {
            bool enabled = page != null && page.Enabled && !String.IsNullOrWhiteSpace(page.Slug);
            return new ProfilePageView
            {
                Enabled = page?.Enabled ?? false,
                Slug = page?.Slug,
                SuggestedSlug = suggested,
                Path = PathOf(PublicPageKindEnum.Profile, page?.Slug),
                Url = enabled && Config().Enabled ? UrlOf(PublicPageKindEnum.Profile, page!.Slug) : null
            };
        }

        private async Task<string> SuggestSlugAsync(User user, CancellationToken token)
        {
            // Only what a signed-out visitor may see goes into the slug, so a members-only city never leaks into a URL.
            PublicProfileResponse view = await _Services.Views.BuildPublicAsync(user, ProfileViewerEnum.Anonymous, token).ConfigureAwait(false);
            string slug = PublicSlugRules.Derive("member", user.DisplayName, view.City, view.Region);
            for (int i = 1; i <= 10; i++)
            {
                string candidate = i == 1 ? slug : PublicSlugRules.WithSuffix(slug, i);
                if (!await _Services.Database.PublicPages.SlugTakenAsync(PublicPageKindEnum.Profile, candidate, user.Id, token).ConfigureAwait(false)) return candidate;
            }
            return PublicSlugRules.WithSuffix(slug.Length > 70 ? slug.Substring(0, 70).TrimEnd('-') : slug, 1000 + Random.Shared.Next(9000));
        }

        private List<PublicPageKindEnum> Kinds()
        {
            List<PublicPageKindEnum> kinds = new List<PublicPageKindEnum>();
            if (!Config().Enabled) return kinds;
            if (_Services.Property.Property.Features.Directory) kinds.Add(PublicPageKindEnum.Venue);
            if (_Services.Property.Property.Features.Events) kinds.Add(PublicPageKindEnum.Event);
            kinds.Add(PublicPageKindEnum.Profile);
            return kinds;
        }

        private static PublicPageResult SitemapNotFound()
        {
            return new PublicPageResult { StatusCode = 404, ContentType = "text/plain; charset=utf-8", Body = "Not found.", CacheControl = "no-cache" };
        }

        private static string Xml(string value)
        {
            return SecurityElement.Escape(value) ?? String.Empty;
        }

        private PublicPageResult Redirect(PublicPageKindEnum kind, string slug)
        {
            return new PublicPageResult { StatusCode = 301, Location = PathOf(kind, slug), Body = String.Empty, CacheControl = "public, max-age=3600" };
        }

        private PublicPageBrand Brand()
        {
            PropertyConfiguration property = _Services.Property.Property;
            string api = _Services.Property.ApiOrigin;
            string site = _Services.Property.PublicBaseUrl;
            string? logo = PropertyLoader.AssetFileName(property.Brand?.Logo);
            string? icon = PropertyLoader.AssetFileName(property.Brand?.Icon);
            return new PublicPageBrand
            {
                Name = property.Name,
                Tagline = property.Tagline,
                LogoUrl = logo == null ? null : api + PropertyRoutes.AssetPathPrefix + Uri.EscapeDataString(logo),
                IconUrl = icon == null ? null : api + PropertyRoutes.AssetPathPrefix + Uri.EscapeDataString(icon),
                SiteUrl = site,
                JoinUrl = site + (_Services.Access.Current.PurchasesEnabled ? "/join" : "/sign-up"),
                SignInUrl = site + "/sign-in",
                Colors = _Services.Property.BrandColors,
                Locale = String.IsNullOrWhiteSpace(property.Locale) ? "en-US" : property.Locale,
                MemberWord = String.IsNullOrWhiteSpace(property.Terminology?.Member) ? "member" : property.Terminology!.Member
            };
        }

        private PublicPageResult Page(PublicPageDocument page, int status = 200)
        {
            PublicPageBrand brand = Brand();
            if (String.IsNullOrWhiteSpace(page.ImageUrl))
            {
                page.ImageUrl = brand.LogoUrl ?? String.Empty;
                page.ImageAlt = brand.Name;
            }
            return new PublicPageResult { StatusCode = status, Body = PublicPageRenderer.Render(brand, page), CacheControl = "no-cache" };
        }

        private PublicPageResult NotFound()
        {
            PublicPageBrand brand = Brand();
            string main = "<section class=\"card\"><h1>" + PublicPageRenderer.Attr(PublicPageText.NotFoundHeading) + "</h1><p>" + PublicPageRenderer.Attr(PublicPageText.NotFoundBody) +
                "</p><p><a href=\"" + PublicPageRenderer.Attr(brand.SiteUrl + "/") + "\">" + PublicPageRenderer.Attr(PublicPageText.Home) + "</a></p></section>";
            return Page(new PublicPageDocument
            {
                Title = PublicPageText.Title(PublicPageText.NotFoundHeading, brand.Name),
                Description = PublicPageText.NotFoundBody,
                MainHtml = main,
                NoIndex = true
            }, 404);
        }

        private string? Absolute(string? url)
        {
            if (String.IsNullOrWhiteSpace(url)) return null;
            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return url;
            if (url.StartsWith('/')) return _Services.Property.ApiOrigin + url;
            return null;
        }

        private string? ImageOf(MediaView? media)
        {
            if (media?.Image?.Variants == null) return null;
            foreach (string size in new[] { "large", "medium", "thumb" })
                if (media.Image.Variants.TryGetValue(size, out MediaVariantView? v)) return Absolute(v.JpegUrl ?? v.WebpUrl);
            MediaVariantView? any = media.Image.Variants.Values.FirstOrDefault();
            return any == null ? null : Absolute(any.JpegUrl ?? any.WebpUrl);
        }

        private static string Excerpt(params string?[] candidates)
        {
            string text = candidates.FirstOrDefault(c => !String.IsNullOrWhiteSpace(c)) ?? String.Empty;
            text = System.Text.RegularExpressions.Regex.Replace(text, "[#*_`>\\[\\]]", String.Empty);
            text = System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
            if (text.Length <= 300) return text;
            int cut = text.LastIndexOf(' ', 297);
            return text.Substring(0, cut > 200 ? cut : 297).TrimEnd() + "...";
        }

        private static string Json(Dictionary<string, object?> value)
        {
            return JsonSerializer.Serialize(Compact(value), _Json);
        }

        private static object? Compact(object? value)
        {
            if (value is Dictionary<string, object?> map)
            {
                Dictionary<string, object?> result = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, object?> pair in map)
                {
                    object? v = Compact(pair.Value);
                    if (v == null) continue;
                    if (v is string s && s.Length == 0) continue;
                    if (v is List<object?> l && l.Count == 0) continue;
                    result[pair.Key] = v;
                }
                return result.Count == 0 ? null : result;
            }
            if (value is List<object?> list)
            {
                List<object?> result = list.Select(Compact).Where(v => v != null).ToList();
                return result;
            }
            return value;
        }

        private static string Facts(List<KeyValuePair<string, string>> facts)
        {
            if (facts.Count == 0) return String.Empty;
            StringBuilder html = new StringBuilder("<dl class=\"facts\">");
            foreach (KeyValuePair<string, string> fact in facts) html.Append("<dt>").Append(PublicPageRenderer.Attr(fact.Key)).Append("</dt><dd>").Append(fact.Value).Append("</dd>");
            return html.Append("</dl>").ToString();
        }

        private static string Link(string url, string text)
        {
            return "<a href=\"" + PublicPageRenderer.Attr(url) + "\" rel=\"noopener\">" + PublicPageRenderer.Attr(text) + "</a>";
        }

        private static bool IsHttpUrl(string? value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        }

        private async Task<PublicPageResult> VenuePageAsync(string key, CancellationToken token)
        {
            Venue? venue = await _Services.Database.DirectoryListings.Venues.ReadBySlugAsync(key, token).ConfigureAwait(false);
            if (!VenueHasPage(venue)) return NotFound();
            if (!String.Equals(venue!.Slug, key, StringComparison.Ordinal)) return Redirect(PublicPageKindEnum.Venue, venue.Slug);
            DirectoryConfig config = await _Services.Directory.Config.CurrentAsync(token).ConfigureAwait(false);
            List<DirectoryProgram> programs = await _Services.Directory.Config.ProgramsAsync(token).ConfigureAwait(false);
            ProfileField? rankField = await _Services.Directory.Config.RankFieldAsync(token).ConfigureAwait(false);
            bool showPhone = await _Services.Directory.Config.FlagAsync(DirectoryPropertySettings.PublicShowsContact, token).ConfigureAwait(false);

            // Staff-curated photos only (member media is a separate target); make sure the public copies exist first.
            List<MediaAttachment> attached = await _Services.Database.Media.ListByTargetsAsync(TargetTypeEnum.Venue, new[] { venue.Id }, token).ConfigureAwait(false);
            if (attached.Any(a => a.Status == MediaStatusEnum.Ready && !a.IsPublic && !a.IsPrivate && !a.IsDeleted && !a.IsRetained))
                await _Services.Media.SyncTargetPublicAsync(TargetTypeEnum.Venue, venue.Id, true, token).ConfigureAwait(false);
            Dictionary<string, List<MediaView>> photoViews = await _Services.Media.ListViewsForTargetsAsync(TargetTypeEnum.Venue, new[] { venue.Id }, true, false, token).ConfigureAwait(false);
            List<MediaView> photos = photoViews.TryGetValue(venue.Id, out List<MediaView>? list) ? list.Take(Math.Max(0, _Services.Settings.Directory.MaxPhotos)).ToList() : new List<MediaView>();

            EnumerationRequest request = _Services.Enumeration.Prepare(new EnumerationQuery { MaxResults = MaxInstructors }, DirectoryCollections.VenueInstructors, false, venue.Id);
            InstructorFilter filter = new InstructorFilter
            {
                VenueId = venue.Id,
                Audiences = new List<ContentAudienceEnum> { ContentAudienceEnum.Public, ContentAudienceEnum.SignedIn, ContentAudienceEnum.Members },
                LiveOnly = true
            };
            List<Instructor> instructors = (await _Services.Database.DirectoryListings.Instructors.EnumerateAsync(request, filter, token).ConfigureAwait(false)).Objects.Take(MaxInstructors).ToList();

            bool exact = venue.AddressVisibility == AddressVisibilityEnum.Exact;
            AddressView address = DirectoryViewBuilder.Address(venue, exact);
            if (!exact) address.Formatted = String.Join(", ", new[] { venue.Locality, venue.Region, venue.CountryCode }.Where(p => !String.IsNullOrWhiteSpace(p)));
            GeoView geo = DirectoryViewBuilder.Geo(venue, false);
            string url = UrlOf(PublicPageKindEnum.Venue, venue.Slug)!;
            string site = _Services.Property.PublicBaseUrl;
            List<string> images = photos.Select(ImageOf).Where(u => u != null).Select(u => u!).ToList();
            List<string> programLabels = venue.Programs.Select(k => DirectoryConfigService.Option(programs, k).Label).Where(l => !String.IsNullOrWhiteSpace(l)).ToList();
            double? average = venue.RatingCount > 0 ? Math.Round((double)venue.RatingTotal / venue.RatingCount, 1) : null;

            StringBuilder main = new StringBuilder();
            if (images.Count > 0) main.Append(PublicPageRenderer.Img(images[0], photos[0].AltText ?? venue.Name, "cover", false));
            main.Append("<section class=\"card\"><p class=\"eyebrow\">").Append(PublicPageRenderer.Attr(config.VenueLabel)).Append("</p><h1>").Append(PublicPageRenderer.Attr(venue.Name)).Append("</h1>");
            string subtitle = String.Join(" | ", new[] { venue.Affiliation, address.Formatted }.Where(p => !String.IsNullOrWhiteSpace(p)));
            if (subtitle.Length > 0) main.Append("<p class=\"subtitle\">").Append(PublicPageRenderer.Attr(subtitle)).Append("</p>");
            if (programLabels.Count > 0) main.Append("<ul class=\"chips\" aria-label=\"").Append(PublicPageRenderer.Attr(PublicPageText.Programs)).Append("\">")
                .Append(String.Join(String.Empty, programLabels.Select(l => "<li>" + PublicPageRenderer.Attr(l) + "</li>"))).Append("</ul>");
            if (!String.IsNullOrWhiteSpace(venue.Summary)) main.Append("<p>").Append(PublicPageRenderer.Attr(venue.Summary)).Append("</p>");
            main.Append("</section>");
            if (!String.IsNullOrWhiteSpace(venue.Description))
                main.Append("<section class=\"card\"><h2>").Append(PublicPageRenderer.Attr(PublicPageText.About)).Append("</h2><p class=\"description\">").Append(PublicPageRenderer.Attr(venue.Description)).Append("</p></section>");

            List<KeyValuePair<string, string>> facts = new List<KeyValuePair<string, string>>();
            if (!String.IsNullOrWhiteSpace(address.Formatted)) facts.Add(new KeyValuePair<string, string>(exact ? PublicPageText.Address : PublicPageText.Location, PublicPageRenderer.Attr(address.Formatted)));
            string? visitors = PublicPageText.VisitorPolicy(venue.VisitorPolicy.ToString());
            if (visitors != null) facts.Add(new KeyValuePair<string, string>(PublicPageText.Visitors, PublicPageRenderer.Attr(visitors + (String.IsNullOrWhiteSpace(venue.VisitorPolicyNote) ? String.Empty : ". " + venue.VisitorPolicyNote))));
            if (average != null) facts.Add(new KeyValuePair<string, string>(PublicPageText.Rating, PublicPageRenderer.Attr(PublicPageText.RatingSummary(average.Value, venue.RatingCount))));
            if (IsHttpUrl(venue.WebsiteUrl)) facts.Add(new KeyValuePair<string, string>(PublicPageText.Website, Link(venue.WebsiteUrl!, venue.WebsiteUrl!)));
            if (showPhone && !String.IsNullOrWhiteSpace(venue.Phone)) facts.Add(new KeyValuePair<string, string>(PublicPageText.Phone, PublicPageRenderer.Attr(venue.Phone)));
            List<DirectorySocialLink> social = venue.SocialLinks.Where(l => IsHttpUrl(l.Url)).OrderBy(l => l.Position).ToList();
            if (social.Count > 0) facts.Add(new KeyValuePair<string, string>(PublicPageText.Links, String.Join(" | ", social.Select(l => Link(l.Url, l.Kind.ToString())))));
            if (facts.Count > 0) main.Append("<section class=\"card\"><h2>").Append(PublicPageRenderer.Attr(PublicPageText.Details)).Append("</h2>").Append(Facts(facts)).Append("</section>");
            string? scheduleText = venue.Schedule == null ? venue.ScheduleText : VenueSchedules.Text(venue.Schedule);
            if (!String.IsNullOrWhiteSpace(scheduleText))
                main.Append("<section class=\"card\"><h2>").Append(PublicPageRenderer.Attr(PublicPageText.Schedule)).Append("</h2><p class=\"description\">").Append(PublicPageRenderer.Attr(scheduleText)).Append("</p></section>");
            if (instructors.Count > 0)
            {
                main.Append("<section class=\"card\"><h2>").Append(PublicPageRenderer.Attr(PublicPageText.Instructors(config.InstructorLabelPlural))).Append("</h2><ul class=\"people\">");
                foreach (Instructor i in instructors)
                {
                    string rank = rankField != null && i.RankKey != null ? ProfileValueService.FormatRank(rankField, i.RankKey, i.RankLevel ?? 0) : String.Empty;
                    main.Append("<li><strong>").Append(PublicPageRenderer.Attr(i.DisplayName)).Append("</strong>");
                    if (rank.Length > 0) main.Append(" <span class=\"subtitle\">").Append(PublicPageRenderer.Attr(rank)).Append("</span>");
                    main.Append("</li>");
                }
                main.Append("</ul></section>");
            }
            if (images.Count > 1)
            {
                main.Append("<section class=\"card\"><h2>").Append(PublicPageRenderer.Attr(PublicPageText.Photos)).Append("</h2><ul class=\"photos\">");
                for (int i = 1; i < photos.Count; i++)
                {
                    string? img = ImageOf(photos[i]);
                    if (img != null) main.Append("<li>").Append(PublicPageRenderer.Img(img, photos[i].AltText ?? venue.Name, "photo", true)).Append("</li>");
                }
                main.Append("</ul></section>");
            }

            main.Append(await GalleryHtmlAsync(TargetTypeEnum.VenueMedia, venue.Id, token).ConfigureAwait(false));

            Dictionary<string, object?> ld = new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "SportsActivityLocation",
                ["@id"] = url,
                ["name"] = venue.Name,
                ["description"] = Excerpt(venue.Summary, venue.Description),
                ["url"] = url,
                ["image"] = images.Cast<object?>().ToList(),
                ["telephone"] = showPhone ? venue.Phone : null,
                ["address"] = new Dictionary<string, object?>
                {
                    ["@type"] = "PostalAddress",
                    ["streetAddress"] = exact ? String.Join(", ", new[] { venue.Line1, venue.Line2 }.Where(p => !String.IsNullOrWhiteSpace(p))) : null,
                    ["addressLocality"] = venue.Locality,
                    ["addressRegion"] = venue.Region,
                    ["postalCode"] = exact ? venue.PostalCode : null,
                    ["addressCountry"] = venue.CountryCode
                },
                ["geo"] = geo.Lat != null && geo.Lng != null && geo.Precision != GeoPrecisionEnum.None
                    ? new Dictionary<string, object?> { ["@type"] = "GeoCoordinates", ["latitude"] = geo.Lat, ["longitude"] = geo.Lng }
                    : null,
                ["sameAs"] = social.Select(l => (object?)l.Url).Concat(IsHttpUrl(venue.WebsiteUrl) ? new object?[] { venue.WebsiteUrl } : Array.Empty<object?>()).ToList(),
                ["aggregateRating"] = average != null
                    ? new Dictionary<string, object?> { ["@type"] = "AggregateRating", ["ratingValue"] = average, ["reviewCount"] = venue.RatingCount, ["bestRating"] = 5, ["worstRating"] = 1 }
                    : null
            };
            return Page(new PublicPageDocument
            {
                Title = PublicPageText.Title(venue.Name + (String.IsNullOrWhiteSpace(venue.Locality) ? String.Empty : ", " + venue.Locality), _Services.Property.Name),
                Description = Excerpt(venue.Summary, venue.Description, venue.Name + (String.IsNullOrWhiteSpace(address.Formatted) ? String.Empty : ", " + address.Formatted)),
                CanonicalUrl = url,
                ImageUrl = images.FirstOrDefault() ?? String.Empty,
                ImageAlt = venue.Name,
                OgType = "website",
                JsonLd = Json(ld),
                MainHtml = main.ToString(),
                AppUrl = site + "/app/venues/" + Uri.EscapeDataString(venue.Id),
                AppLabel = PublicPageText.OpenVenueInApp
            });
        }

        private async Task<PublicPageResult> EventPageAsync(string key, string? sourceIp, CancellationToken token)
        {
            EventRecord? e = await _Services.Database.Events.Records.ReadBySlugAsync(key, token).ConfigureAwait(false);
            if (!EventHasPage(e)) return NotFound();
            if (!String.Equals(e!.Slug, key, StringComparison.Ordinal)) return Redirect(PublicPageKindEnum.Event, e.Slug);
            PublicEventView view = (await _Services.Events.Views.PublicAsync(_Services.Actors.Anonymous(sourceIp), new List<EventRecord> { e }, token).ConfigureAwait(false))[0];
            string url = UrlOf(PublicPageKindEnum.Event, e.Slug)!;
            string site = _Services.Property.PublicBaseUrl;
            TimeZoneInfo zone = EventTimeZones.Find(e.TimeZone) ?? TimeZoneInfo.Utc;
            DateTime startLocal = EventTimeZones.ToLocal(e.StartUtc, zone);
            DateTime endLocal = EventTimeZones.ToLocal(e.EndUtc, zone);
            string when = e.IsAllDay
                ? startLocal.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture) + (endLocal.Date > startLocal.Date.AddDays(1) ? " to " + endLocal.AddDays(-1).ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture) : String.Empty)
                : startLocal.ToString("dddd, MMMM d, yyyy 'at' h:mm tt", CultureInfo.InvariantCulture) + " to " +
                    (endLocal.Date == startLocal.Date ? endLocal.ToString("h:mm tt", CultureInfo.InvariantCulture) : endLocal.ToString("dddd, MMMM d, yyyy 'at' h:mm tt", CultureInfo.InvariantCulture)) +
                    " (" + e.TimeZone + ")";
            string? image = ImageOf(view.Cover);
            string? locationText = view.Location == null ? null : String.Join(", ", new[] { view.Location.Venue?.Name ?? view.Location.Name, view.Location.Address?.Formatted }.Where(p => !String.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal));
            Venue? venue = view.Location?.Venue != null ? await _Services.Database.DirectoryListings.Venues.ReadAsync(view.Location.Venue.Id, token).ConfigureAwait(false) : null;
            string? venueUrl = VenueUrl(venue);
            bool canceled = e.Status == EventStatusEnum.Canceled;
            bool ended = e.EndUtc < DateTime.UtcNow;

            StringBuilder main = new StringBuilder();
            if (image != null) main.Append(PublicPageRenderer.Img(image, view.Cover?.AltText ?? e.Title, "cover", false));
            main.Append("<section class=\"card\">");
            if (view.Category != null && !String.IsNullOrWhiteSpace(view.Category.Label)) main.Append("<p class=\"eyebrow\">").Append(PublicPageRenderer.Attr(view.Category.Label)).Append("</p>");
            main.Append("<h1>").Append(PublicPageRenderer.Attr(e.Title)).Append("</h1>");
            main.Append("<p class=\"subtitle\"><time datetime=\"").Append(PublicPageRenderer.Attr(IsoLocal(e.StartUtc, zone, e.IsAllDay))).Append("\">").Append(PublicPageRenderer.Attr(when)).Append("</time></p>");
            if (canceled) main.Append("<p class=\"status\" role=\"status\">").Append(PublicPageRenderer.Attr(PublicPageText.Canceled)).Append("</p>");
            else if (ended) main.Append("<p class=\"status\" role=\"status\">").Append(PublicPageRenderer.Attr(PublicPageText.Ended)).Append("</p>");
            if (!String.IsNullOrWhiteSpace(e.Summary)) main.Append("<p>").Append(PublicPageRenderer.Attr(e.Summary)).Append("</p>");
            main.Append("</section>");
            if (!String.IsNullOrWhiteSpace(e.Description))
                main.Append("<section class=\"card\"><h2>").Append(PublicPageRenderer.Attr(PublicPageText.About)).Append("</h2><p class=\"description\">").Append(PublicPageRenderer.Attr(e.Description)).Append("</p></section>");
            List<KeyValuePair<string, string>> facts = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(PublicPageText.When, PublicPageRenderer.Attr(when)),
                new KeyValuePair<string, string>(PublicPageText.Format, PublicPageRenderer.Attr(PublicPageText.EventFormat(e.Format.ToString()) + (String.IsNullOrWhiteSpace(view.OnlinePlatformLabel) ? String.Empty : " (" + view.OnlinePlatformLabel + ")")))
            };
            if (!String.IsNullOrWhiteSpace(locationText)) facts.Add(new KeyValuePair<string, string>(PublicPageText.Location, venueUrl != null ? Link(venueUrl, locationText!) : PublicPageRenderer.Attr(locationText)));
            if (view.SpotsLeft != null && !canceled && !ended) facts.Add(new KeyValuePair<string, string>(PublicPageText.Spots, PublicPageRenderer.Attr(PublicPageText.SpotsLeft(view.SpotsLeft.Value))));
            main.Append("<section class=\"card\"><h2>").Append(PublicPageRenderer.Attr(PublicPageText.Details)).Append("</h2>").Append(Facts(facts)).Append("</section>");

            object? place = null;
            if (e.Format != EventFormatEnum.Online && view.Location != null)
            {
                place = new Dictionary<string, object?>
                {
                    ["@type"] = "Place",
                    ["name"] = view.Location.Venue?.Name ?? view.Location.Name,
                    ["url"] = venueUrl,
                    ["address"] = view.Location.Address == null ? null : new Dictionary<string, object?>
                    {
                        ["@type"] = "PostalAddress",
                        ["streetAddress"] = String.Join(", ", new[] { view.Location.Address.Line1, view.Location.Address.Line2 }.Where(p => !String.IsNullOrWhiteSpace(p))),
                        ["addressLocality"] = view.Location.Address.Locality,
                        ["addressRegion"] = view.Location.Address.Region,
                        ["postalCode"] = view.Location.Address.PostalCode,
                        ["addressCountry"] = view.Location.Address.CountryCode
                    }
                };
            }
            object? virtualPlace = e.Format == EventFormatEnum.InPerson ? null : new Dictionary<string, object?> { ["@type"] = "VirtualLocation", ["url"] = url };
            List<object?> locations = new List<object?>();
            if (place != null) locations.Add(place);
            if (virtualPlace != null) locations.Add(virtualPlace);
            Dictionary<string, object?> ld = new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "Event",
                ["@id"] = url,
                ["name"] = e.Title,
                ["description"] = Excerpt(e.Summary, e.Description),
                ["url"] = url,
                ["image"] = image == null ? null : new List<object?> { image },
                ["startDate"] = IsoLocal(e.StartUtc, zone, e.IsAllDay),
                ["endDate"] = IsoLocal(e.IsAllDay ? e.EndUtc.AddDays(-1) : e.EndUtc, zone, e.IsAllDay),
                ["eventStatus"] = canceled ? "https://schema.org/EventCancelled" : "https://schema.org/EventScheduled",
                ["eventAttendanceMode"] = e.Format == EventFormatEnum.Online ? "https://schema.org/OnlineEventAttendanceMode"
                    : e.Format == EventFormatEnum.Hybrid ? "https://schema.org/MixedEventAttendanceMode" : "https://schema.org/OfflineEventAttendanceMode",
                ["location"] = locations.Count == 1 ? locations[0] : locations.Count == 0 ? null : locations,
                ["organizer"] = new Dictionary<string, object?> { ["@type"] = "Organization", ["name"] = _Services.Property.Name, ["url"] = site + "/" }
            };
            return Page(new PublicPageDocument
            {
                Title = PublicPageText.Title(e.Title, _Services.Property.Name),
                Description = Excerpt(e.Summary, e.Description, e.Title + ", " + when),
                CanonicalUrl = url,
                ImageUrl = image ?? String.Empty,
                ImageAlt = e.Title,
                OgType = "website",
                JsonLd = Json(ld),
                MainHtml = main.ToString(),
                AppUrl = site + "/app/events/" + Uri.EscapeDataString(e.Id),
                AppLabel = PublicPageText.OpenEventInApp
            });
        }

        private async Task<PublicPageResult> ProfilePageAsync(string key, CancellationToken token)
        {
            ProfilePage? page = await _Services.Database.PublicPages.ReadProfileBySlugAsync(key, token).ConfigureAwait(false);
            if (page == null || !page.Enabled || String.IsNullOrWhiteSpace(page.Slug)) return NotFound();
            User? user = await _Services.Database.Users.ReadAsync(page.UserId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted || user.IsBanned) return NotFound();
            if (!String.Equals(page.Slug, key, StringComparison.Ordinal)) return Redirect(PublicPageKindEnum.Profile, page.Slug);
            PublicProfileResponse view = await _Services.Views.BuildPublicAsync(user, ProfileViewerEnum.Anonymous, token).ConfigureAwait(false);
            string url = UrlOf(PublicPageKindEnum.Profile, page.Slug)!;
            string site = _Services.Property.PublicBaseUrl;
            ProfileField? rankField = _Services.ProfileFields.LegacyField("rank", ProfileViewerEnum.Anonymous);
            string? rank = view.Rank != null && rankField != null ? ProfileValueService.FormatRank(rankField, view.Rank, view.RankLevel) : null;
            string? place = String.Join(", ", new[] { view.City, view.Region, view.Country }.Where(p => !String.IsNullOrWhiteSpace(p)));
            if (place.Length == 0) place = null;
            string? avatar = Absolute(view.AvatarUrl);
            string? affiliationLabel = _Services.ProfileFields.LegacyField("affiliation", ProfileViewerEnum.Anonymous)?.Label;

            List<ProfileField> fields = await _Services.ProfileValues.FieldsForAsync(ProfileMapKindEnum.Profile, ProfileViewerEnum.Anonymous, token).ConfigureAwait(false);
            HashSet<string> headline = new HashSet<string>(new[] { "rank", "affiliation", "city", "region", "country" }, StringComparer.OrdinalIgnoreCase);
            List<KeyValuePair<string, string>> facts = new List<KeyValuePair<string, string>>();
            foreach (ProfileField field in fields)
            {
                // No contact details and nothing that is not plain text (images, history lists) on the public page.
                if (headline.Contains(field.Key) || field.Type == ProfileFieldTypeEnum.Email || field.Type == ProfileFieldTypeEnum.Phone || field.Type == ProfileFieldTypeEnum.Image ||
                    field.Type == ProfileFieldTypeEnum.History) continue;
                if (!view.Profile.TryGetValue(field.Key, out object? value)) continue;
                string? text = ProfileValueService.FormatValue(field, value);
                if (text == null) continue;
                facts.Add(new KeyValuePair<string, string>(field.Label, field.Type == ProfileFieldTypeEnum.Url && IsHttpUrl(text) ? Link(text, text) : PublicPageRenderer.Attr(text)));
            }

            StringBuilder main = new StringBuilder();
            main.Append("<section class=\"card hero\">");
            if (avatar != null) main.Append(PublicPageRenderer.Img(avatar, view.DisplayName, "avatar", false));
            main.Append("<div><h1>").Append(PublicPageRenderer.Attr(view.DisplayName)).Append("</h1>");
            if (rank != null) main.Append("<p class=\"subtitle\">").Append(PublicPageRenderer.Attr((rankField?.Label ?? String.Empty).Length > 0 ? rankField!.Label + ": " + rank : rank)).Append("</p>");
            if (!String.IsNullOrWhiteSpace(view.Affiliation)) main.Append("<p class=\"subtitle\">").Append(PublicPageRenderer.Attr(String.IsNullOrWhiteSpace(affiliationLabel) ? view.Affiliation : affiliationLabel + ": " + view.Affiliation)).Append("</p>");
            if (place != null) main.Append("<p class=\"subtitle\">").Append(PublicPageRenderer.Attr(place)).Append("</p>");
            main.Append("</div></section>");
            if (!String.IsNullOrWhiteSpace(view.Bio))
                main.Append("<section class=\"card\"><h2>").Append(PublicPageRenderer.Attr(PublicPageText.About)).Append("</h2><p class=\"description\">").Append(PublicPageRenderer.Attr(view.Bio)).Append("</p></section>");
            if (facts.Count > 0) main.Append("<section class=\"card\"><h2>").Append(PublicPageRenderer.Attr(PublicPageText.Details)).Append("</h2>").Append(Facts(facts)).Append("</section>");

            main.Append(await GalleryHtmlAsync(TargetTypeEnum.ProfileMedia, user.Id, token).ConfigureAwait(false));

            Dictionary<string, object?> person = new Dictionary<string, object?>
            {
                ["@type"] = "Person",
                ["name"] = view.DisplayName,
                ["image"] = avatar,
                ["description"] = Excerpt(view.Bio),
                ["url"] = url,
                ["affiliation"] = String.IsNullOrWhiteSpace(view.Affiliation) ? null : new Dictionary<string, object?> { ["@type"] = "Organization", ["name"] = view.Affiliation },
                ["homeLocation"] = place == null ? null : new Dictionary<string, object?>
                {
                    ["@type"] = "Place",
                    ["address"] = new Dictionary<string, object?> { ["@type"] = "PostalAddress", ["addressLocality"] = view.City, ["addressRegion"] = view.Region, ["addressCountry"] = view.Country }
                }
            };
            Dictionary<string, object?> ld = new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "ProfilePage",
                ["@id"] = url,
                ["url"] = url,
                ["name"] = view.DisplayName,
                ["dateCreated"] = page.CreatedUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["dateModified"] = (page.UpdatedUtc > user.UpdatedUtc ? page.UpdatedUtc : user.UpdatedUtc).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["mainEntity"] = person
            };
            string summary = String.Join(", ", new[] { rank, view.Affiliation, place }.Where(p => !String.IsNullOrWhiteSpace(p)));
            return Page(new PublicPageDocument
            {
                Title = PublicPageText.Title(view.DisplayName, _Services.Property.Name),
                Description = Excerpt(view.Bio, view.DisplayName + (summary.Length > 0 ? ", " + summary : String.Empty)),
                CanonicalUrl = url,
                ImageUrl = avatar ?? String.Empty,
                ImageAlt = view.DisplayName,
                OgType = "profile",
                JsonLd = Json(ld),
                MainHtml = main.ToString(),
                AppUrl = site + "/app/members/" + Uri.EscapeDataString(user.Id),
                AppLabel = PublicPageText.OpenProfileInApp
            });
        }

        private static string IsoLocal(DateTime utc, TimeZoneInfo zone, bool dateOnly)
        {
            DateTime local = EventTimeZones.ToLocal(utc, zone);
            if (dateOnly) return local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            TimeSpan offset = zone.GetUtcOffset(utc);
            return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
