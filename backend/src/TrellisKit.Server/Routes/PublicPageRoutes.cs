namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.PublicPages;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Public landing page routes (docs/PHASE4_CONTRACT.md section 18): the server-rendered venue, event, and member
    /// profile pages, the sitemap, and robots.txt under the fixed prefix /api/v1/public/pages (the website proxy maps
    /// /{path}/{slug}, /sitemap.xml, /sitemap-*.xml, and /robots.txt onto them), plus the member's page settings, the
    /// slug check, and the admin member page routes.
    /// </summary>
    public class PublicPageRoutes
    {
        #region Private-Members

        private const string _Tag = "Public pages";
        private const string _Base = Constants.ApiPrefix + "/public/pages";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public PublicPageRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the routes.
        /// </summary>
        /// <param name="server">Webserver.</param>
        /// <exception cref="ArgumentNullException">The server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            WatsonWebserver.Core.Routing.RoutingGroup pre = server.Routes.PreAuthentication;
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;
            pre.Parameter.Add(HttpMethod.GET, _Base + "/resolve/{path}/{slug}", ResolveAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Render a public landing page", _Tag, "The page at /{path}/{slug}: path is one of the property's publicPages path words (venuesPath, eventsPath, profilesPath). 200 a complete HTML document " +
                    "(title, description, canonical, Open Graph, Twitter, JSON-LD); 301 to the current slug for a previous slug; 404 an HTML not found page (noindex). A path that is not a public page path answers 404 with " +
                    "X-Accel-Redirect: /spa.html so the website proxy serves the website instead. Cache-Control: public, max-age=300. Rate limited per IP (rateLimits.publicPagesPerMinute). Anonymous.")
                    .PathParam("path", "First path segment").PathParam("slug", "Slug").File("text/html", "Page").Resp(301, "Moved Permanently (previous slug)").Resp(404, "Not found page or X-Accel-Redirect").Resp(429, "RateLimited"));
            pre.Static.Add(HttpMethod.GET, _Base + "/sitemap.xml", SitemapIndexAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Sitemap index", _Tag, "Served at /sitemap.xml: the website's /sitemap-pages.xml plus /sitemap-{venues|events|profiles}-{n}.xml (10,000 URLs each). Cache-Control: public, max-age=3600. Anonymous.")
                    .File("application/xml", "Sitemap index").Resp(429, "RateLimited"));
            pre.Parameter.Add(HttpMethod.GET, _Base + "/sitemaps/{name}", SitemapAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Child sitemap", _Tag, "Served at /sitemap-{name}: venues-{n}.xml, events-{n}.xml, or profiles-{n}.xml (loc, lastmod, changefreq, priority). Events leave the sitemap once they end. Anonymous.")
                    .PathParam("name", "For example venues-1.xml").File("application/xml", "Sitemap").Resp(404, "Unknown sitemap").Resp(429, "RateLimited"));
            pre.Static.Add(HttpMethod.GET, _Base + "/well-known/apple-app-site-association", AppleAppSiteAssociationAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Apple app site association", _Tag, "Served at /.well-known/apple-app-site-association: { webcredentials: { apps: [\"<mobile.appleTeamId>.<mobile.bundleId>\"] } }, so iOS offers to save and autofill the app's passwords for the website domain. 404 when mobile.appleTeamId or mobile.bundleId is not set. Cache-Control: public, max-age=3600. Anonymous.")
                    .File("application/json", "Association file").Resp(404, "NotFound"));
            pre.Static.Add(HttpMethod.GET, _Base + "/well-known/assetlinks.json", AssetLinksAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Android asset links", _Tag, "Served at /.well-known/assetlinks.json: the delegate_permission/common.get_login_creds statement for mobile.androidPackage and mobile.androidCertFingerprints, so Android shares saved passwords between the app and the website. 404 when either is not set. Cache-Control: public, max-age=3600. Anonymous.")
                    .File("application/json", "Asset links").Resp(404, "NotFound"));
            pre.Static.Add(HttpMethod.GET, _Base + "/robots.txt", RobotsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("robots.txt", _Tag, "Served at /robots.txt: allows the site, disallows /app, /admin, and /api (except the public media, avatar, and brand image paths), and names the sitemap. Anonymous.")
                    .File("text/plain", "robots.txt").Resp(429, "RateLimited"));

            post.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/me/public-page", GetMineAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Your public profile page", _Tag, "{ enabled (default false), slug, suggestedSlug, path, url }. 404 NotAvailable when publicPages.enabled is off. Signed in.")
                    .Returns("ProfilePageView").Resp(404, "NotAvailable"));
            post.Static.Add(HttpMethod.PUT, Constants.ApiPrefix + "/me/public-page", PutMineAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Change your public profile page", _Tag, "Body { enabled?, slug? } (omitted fields stay). Turning it on without a slug uses suggestedSlug; a changed slug keeps the previous one as a redirect. " +
                    "The page shows only what signed-out visitors may see. Signed in.").Body("ProfilePageRequest").Returns("ProfilePageView").Resp(400, "ValidationFailed (fieldErrors.slug)").Resp(404, "NotAvailable").Resp(409, "SlugInUse"));
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/public-pages/slugs/check", CheckAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Check a public page slug", _Tag, "Body { kind: Venue | Event | Profile, slug, id? (the object being edited) }. Returns { kind, slug, valid, available, problem (required, length, format, reserved, taken), path, url }. Signed in.")
                    .Body("SlugCheckRequest").Returns("SlugCheckView"));
            post.Parameter.Add(HttpMethod.GET, Constants.ApiPrefix + "/admin/members/{id}/public-page", AdminGetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("A member's public profile page", _Tag, "As GET /api/v1/me/public-page for the member. Admin.").PathParam("id", "Member identifier").Returns("ProfilePageView").Resp(404, "NotFound or NotAvailable"));
            post.Parameter.Add(HttpMethod.PUT, Constants.ApiPrefix + "/admin/members/{id}/public-page", AdminPutAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Change a member's public profile page", _Tag, "As PUT /api/v1/me/public-page for the member; audited as profile.public_page. Admin.").PathParam("id", "Member identifier")
                    .Body("ProfilePageRequest").Returns("ProfilePageView").Resp(400, "ValidationFailed").Resp(404, "NotFound or NotAvailable").Resp(409, "SlugInUse"));
        }

        #endregion

        #region Private-Methods

        private static async Task SendAsync(HttpContextBase ctx, PublicPageResult result)
        {
            ctx.Response.StatusCode = result.StatusCode;
            ctx.Response.ContentType = result.ContentType;
            if (result.AccelRedirect != null)
            {
                // The proxy serves its own file with its own headers; only the content type carries over.
                ctx.Response.Headers[PublicPageResult.AccelRedirectHeader] = result.AccelRedirect;
                await ctx.Response.Send(result.Body ?? String.Empty, ctx.Token).ConfigureAwait(false);
                return;
            }
            ctx.Response.Headers["Cache-Control"] = result.CacheControl;
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (result.Location != null) ctx.Response.Headers["Location"] = result.Location;
            if (result.ContentType.StartsWith("text/html", StringComparison.Ordinal))
            {
                ctx.Response.Headers["Content-Security-Policy"] = "default-src 'none'; img-src https: http: data:; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
                ctx.Response.Headers["Vary"] = "Host, X-TK-Property";
            }
            await ctx.Response.Send(result.Body ?? String.Empty, ctx.Token).ConfigureAwait(false);
        }

        private string? Ip(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest).SourceIp;
        }

        private async Task ResolveAsync(HttpContextBase ctx)
        {
            PublicPageResult result = await _Directory.For(ctx).PublicPages.ResolveAsync(RouteHelper.Param(ctx, "path"), RouteHelper.Param(ctx, "slug"), Ip(ctx), ctx.Token).ConfigureAwait(false);
            await SendAsync(ctx, result).ConfigureAwait(false);
        }

        private async Task SitemapIndexAsync(HttpContextBase ctx)
        {
            await SendAsync(ctx, await _Directory.For(ctx).PublicPages.SitemapIndexAsync(Ip(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SitemapAsync(HttpContextBase ctx)
        {
            await SendAsync(ctx, await _Directory.For(ctx).PublicPages.SitemapAsync(RouteHelper.Param(ctx, "name"), Ip(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task AppleAppSiteAssociationAsync(HttpContextBase ctx)
        {
            TrellisKit.Core.PropertyConfig.PropertyMobile mobile = _Directory.For(ctx).Property.Property.Mobile;
            string team = (mobile.AppleTeamId ?? String.Empty).Trim();
            string bundle = (mobile.BundleId ?? String.Empty).Trim();
            if (team.Length == 0 || bundle.Length == 0)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "No app association is configured.").ConfigureAwait(false);
                return;
            }
            object body = new { webcredentials = new { apps = new[] { team + "." + bundle } } };
            await SendWellKnownAsync(ctx, body).ConfigureAwait(false);
        }

        private async Task AssetLinksAsync(HttpContextBase ctx)
        {
            TrellisKit.Core.PropertyConfig.PropertyMobile mobile = _Directory.For(ctx).Property.Property.Mobile;
            string package = (mobile.AndroidPackage ?? String.Empty).Trim();
            List<string> fingerprints = (mobile.AndroidCertFingerprints ?? new List<string>()).Select(x => (x ?? String.Empty).Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct().ToList();
            if (package.Length == 0 || fingerprints.Count == 0)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "No asset links are configured.").ConfigureAwait(false);
                return;
            }
            object body = new[]
            {
                new
                {
                    relation = new[] { "delegate_permission/common.get_login_creds" },
                    target = new { @namespace = "android_app", package_name = package, sha256_cert_fingerprints = fingerprints }
                }
            };
            await SendWellKnownAsync(ctx, body).ConfigureAwait(false);
        }

        private static async Task SendWellKnownAsync(HttpContextBase ctx, object body)
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            ctx.Response.Headers["Cache-Control"] = "public, max-age=3600";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            await ctx.Response.Send(System.Text.Json.JsonSerializer.Serialize(body), ctx.Token).ConfigureAwait(false);
        }

        private async Task RobotsAsync(HttpContextBase ctx)
        {
            await SendAsync(ctx, _Directory.For(ctx).PublicPages.Robots(Ip(ctx))).ConfigureAwait(false);
        }

        private async Task GetMineAsync(HttpContextBase ctx)
        {
            ProfilePageView view = await _Directory.For(ctx).PublicPages.GetProfilePageAsync(RouteHelper.RequireUserId(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task PutMineAsync(HttpContextBase ctx)
        {
            string userId = RouteHelper.RequireUserId(ctx);
            ProfilePageRequest request = RouteHelper.ReadJson<ProfilePageRequest>(ctx);
            RequestInfo info = RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
            ProfilePageView view = await _Directory.For(ctx).PublicPages.UpdateProfilePageAsync(info, userId, request, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task CheckAsync(HttpContextBase ctx)
        {
            SlugCheckRequest request = RouteHelper.ReadJson<SlugCheckRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            SlugCheckView view = await _Directory.For(ctx).PublicPages.CheckSlugAsync(actor, request, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task AdminGetAsync(HttpContextBase ctx)
        {
            ProfilePageView view = await _Directory.For(ctx).PublicPages.GetProfilePageAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task AdminPutAsync(HttpContextBase ctx)
        {
            ProfilePageRequest request = RouteHelper.ReadJson<ProfilePageRequest>(ctx);
            RequestInfo info = RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
            ProfilePageView view = await _Directory.For(ctx).PublicPages.UpdateProfilePageAsync(info, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        #endregion
    }
}
