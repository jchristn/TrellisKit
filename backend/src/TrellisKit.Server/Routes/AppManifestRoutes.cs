namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Serialization;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// GET /api/v1/app/manifest (docs/LIVE_UPDATES_CONTRACT.md section 11.1): server-evaluated feature flags, kill
    /// switches, and the binary version policy for one caller, with ETag and 304. Anonymous or signed in.
    /// </summary>
    public class AppManifestRoutes
    {
        #region Private-Members

        private const string _Tag = "App configuration";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AppManifestRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">The server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/app/manifest", ManifestAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("App content manifest", _Tag, "Query since (content version). Headers X-Client (web, mobile), X-Client-Version, X-Client-Update-Id, X-Client-Platform, X-Client-Channel, X-TK-Install-Id or X-TK-Visitor-Id, and an optional bearer access token. " +
                    "flags are resolved on the server for this caller (targeting data never leaves the server); killSwitches maps every engine feature key to { killed, message }; binary is the minimum and latest native version policy (mobile). " +
                    "strings { upserts, deletes } carries the remote string overrides of this client (web.* or mobile.*) changed after since (all without since); banners { objects, deletes } the complete active list for this caller (targeted on the server, at most appConfig.maxActiveBanners); assets { upserts, deletes } the remote asset descriptors. " +
                    "fullRefresh is true (strings, banners, and assets null) when since is older than the retained change log or more than appConfig.maxDeltaItems items changed; re-enumerate with POST /api/v1/app/strings, banners, and assets enumerate. ETag and If-None-Match (304). Cache-Control: private, max-age=0. Anonymous.")
                    .QueryParam("since", "Cached content version").Bounded("AppManifest", "flags at most appConfig.maxFlags, killSwitches one per engine feature key, strings and assets at most appConfig.maxDeltaItems changes, banners at most appConfig.maxActiveBanners").Resp(304, "Not modified").Resp(429, "RateLimited"));
            server.Routes.PreAuthentication.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/app/strings/enumerate", StringsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate published remote strings", _Tag, "Full refresh of the remote string overrides for a client: items { key, value, contentVersion }, plus contentVersion at the top level. Filter client (web, mobile; default the X-Client header). Same headers as the manifest. Anonymous.")
                    .Body("EnumerationQuery", false).Enumeration("ClientString", "KeyAscending, CreatedDescending, CreatedAscending", "client", false).Resp(400, "ValidationFailed").Resp(429, "RateLimited"));
            server.Routes.PreAuthentication.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/app/banners/enumerate", BannersAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate active banners", _Tag, "Standard enumeration of the finite banner set active for this caller (targeted on the server, at most appConfig.maxActiveBanners total), plus contentVersion. Supports maxResults, skip, scoped continuation, ordering, search, dates, labels and tags. Same headers as the manifest. Anonymous.")
                    .Body("EnumerationQuery", false).Enumeration("ClientBanner", "PriorityDescending, CreatedDescending, CreatedAscending", String.Empty, false).Resp(429, "RateLimited"));
            server.Routes.PreAuthentication.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/app/assets/enumerate", AssetsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate published remote assets", _Tag, "Full refresh of the remote asset descriptors { id, key, contentType, sizeBytes, width, height, sha256, url, altText, darkVariant, contentVersion }, plus contentVersion. Anonymous.")
                    .Body("EnumerationQuery", false).Enumeration("ClientAsset", "KeyAscending, CreatedDescending, CreatedAscending", String.Empty, false).Resp(429, "RateLimited"));
            server.Routes.PreAuthentication.Parameter.Add(HttpMethod.GET, AppContentService.ContentAssetPath + "/{propertyId}/{sha256Hex}", ContentAssetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a remote content asset", _Tag, "Local delivery of a remote asset image (content addressed WebP): Cache-Control public, max-age=31536000, immutable; ETag; supports a single byte Range (206, 416 outside the asset); 404 for unknown hashes. In AWS the CDN serves app-content/ instead. Anonymous.")
                    .PathParam("propertyId", "Property id").PathParam("sha256Hex", "SHA-256 hex").File("image/webp", "The asset image").Resp(206, "Partial content").Resp(304, "Not modified").Resp(404, "NotFound").Resp(416, "RangeNotSatisfiable"));
        }

        #endregion

        #region Private-Methods

        private async Task ManifestAsync(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            int? since = RouteHelper.QueryInt(ctx, "since", errors);
            RouteHelper.ThrowIfErrors(errors);
            string? authorization = ctx.Request.RetrieveHeaderValue("Authorization");
            string? bearer = authorization != null && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization.Substring(7).Trim() : null;
            string client = String.Equals(ctx.Request.RetrieveHeaderValue("X-Client"), "mobile", StringComparison.OrdinalIgnoreCase) ? "mobile" : "web";
            AppManifestInputs inputs = new AppManifestInputs
            {
                Since = since,
                Client = client,
                Platform = Lower(ctx.Request.RetrieveHeaderValue("X-Client-Platform")),
                AppVersion = ctx.Request.RetrieveHeaderValue("X-Client-Version"),
                UpdateId = ctx.Request.RetrieveHeaderValue("X-Client-Update-Id"),
                Channel = ctx.Request.RetrieveHeaderValue("X-Client-Channel"),
                ClientId = ctx.Request.RetrieveHeaderValue("X-TK-Install-Id") ?? ctx.Request.RetrieveHeaderValue("X-TK-Visitor-Id"),
                AccessToken = bearer,
                Country = ctx.Request.RetrieveHeaderValue("CloudFront-Viewer-Country"),
                SourceIp = ClientAddressResolver.Resolve(ctx, services.Settings.Rest.TrustedProxies),
                Property = since == null ? PropertyRoutes.Build(services.Property.Property, MediaRoutes.RequestOrigin(ctx, services.Settings.Rest.Ssl), services.Settings.Updates.CheckIntervalMinutes) : null,
                Origin = MediaRoutes.RequestOrigin(ctx, services.Settings.Rest.Ssl)
            };
            AppManifestView view = await services.LiveUpdates.AppConfig.ManifestAsync(inputs, ctx.Token).ConfigureAwait(false);
            string json = ApiJson.Serialize(view);
            string etag = "\"" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).Substring(0, 32).ToLowerInvariant() + "\"";
            ctx.Response.Headers["ETag"] = etag;
            ctx.Response.Headers["Cache-Control"] = "private, max-age=0";
            if (String.Equals(ctx.Request.RetrieveHeaderValue("If-None-Match"), etag, StringComparison.Ordinal))
            {
                ctx.Response.StatusCode = 304;
                await ctx.Response.Send(ctx.Token).ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send(json, ctx.Token).ConfigureAwait(false);
        }

        private AppManifestInputs Inputs(HttpContextBase ctx, ApplicationServices services)
        {
            string? authorization = ctx.Request.RetrieveHeaderValue("Authorization");
            return new AppManifestInputs
            {
                Client = String.Equals(ctx.Request.RetrieveHeaderValue("X-Client"), "mobile", StringComparison.OrdinalIgnoreCase) ? "mobile" : "web",
                Platform = Lower(ctx.Request.RetrieveHeaderValue("X-Client-Platform")),
                AppVersion = ctx.Request.RetrieveHeaderValue("X-Client-Version"),
                UpdateId = ctx.Request.RetrieveHeaderValue("X-Client-Update-Id"),
                Channel = ctx.Request.RetrieveHeaderValue("X-Client-Channel"),
                ClientId = ctx.Request.RetrieveHeaderValue("X-TK-Install-Id") ?? ctx.Request.RetrieveHeaderValue("X-TK-Visitor-Id"),
                AccessToken = authorization != null && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization.Substring(7).Trim() : null,
                Country = ctx.Request.RetrieveHeaderValue("CloudFront-Viewer-Country"),
                SourceIp = ClientAddressResolver.Resolve(ctx, services.Settings.Rest.TrustedProxies),
                Origin = MediaRoutes.RequestOrigin(ctx, services.Settings.Rest.Ssl)
            };
        }

        private async Task StringsAsync(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            AppManifestInputs inputs = Inputs(ctx, services);
            long version = (await services.Database.AppConfig.ReadStateAsync(ctx.Token).ConfigureAwait(false)).ContentVersion;
            await RouteHelper.SendJsonAsync(ctx, 200, await services.LiveUpdates.Content.EnumeratePublicStringsAsync(RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), inputs.Client, inputs.ClientId, version, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task BannersAsync(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            AppManifestInputs inputs = Inputs(ctx, services);
            long version = (await services.Database.AppConfig.ReadStateAsync(ctx.Token).ConfigureAwait(false)).ContentVersion;
            FlagEvaluationContext context = await services.LiveUpdates.AppConfig.CallerContextAsync(inputs, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await services.LiveUpdates.Content.EnumeratePublicBannersAsync(RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), context, inputs.Client, inputs.ClientId, version, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task AssetsAsync(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            AppManifestInputs inputs = Inputs(ctx, services);
            long version = (await services.Database.AppConfig.ReadStateAsync(ctx.Token).ConfigureAwait(false)).ContentVersion;
            await RouteHelper.SendJsonAsync(ctx, 200, await services.LiveUpdates.Content.EnumeratePublicAssetsAsync(RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), inputs.ClientId, version, inputs.Origin, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ContentAssetAsync(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            string sha = RouteHelper.Param(ctx, "sha256Hex");
            byte[]? bytes = await services.LiveUpdates.Content.ReadContentAssetAsync(RouteHelper.Param(ctx, "propertyId"), sha, ctx.Token).ConfigureAwait(false);
            if (bytes == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Asset not found.").ConfigureAwait(false);
                return;
            }
            string etag = "\"" + sha + "\"";
            ctx.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
            ctx.Response.Headers["ETag"] = etag;
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["Accept-Ranges"] = "bytes";
            if (String.Equals(ctx.Request.RetrieveHeaderValue("If-None-Match"), etag, StringComparison.Ordinal))
            {
                ctx.Response.StatusCode = 304;
                await ctx.Response.Send(ctx.Token).ConfigureAwait(false);
                return;
            }
            int status = 200;
            string? range = ctx.Request.RetrieveHeaderValue("Range");
            // A single byte range (bytes=a-b, bytes=a-, or the suffix bytes=-n); the asset is bounded by
            // appConfig.maxAssetMegabytes, so the stored bytes are sliced in memory. Multiple ranges get the whole asset.
            if (!String.IsNullOrWhiteSpace(range) && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) && !range.Contains(','))
            {
                long size = bytes.LongLength;
                string[] bounds = range.Substring(6).Trim().Split('-');
                bool hasStart = Int64.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out long start);
                bool hasEnd = bounds.Length == 2 && Int64.TryParse(bounds[1], NumberStyles.None, CultureInfo.InvariantCulture, out long _);
                long end = hasEnd ? Int64.Parse(bounds[1], CultureInfo.InvariantCulture) : size - 1;
                if (!hasStart && hasEnd)
                {
                    start = Math.Max(0, size - end);
                    end = size - 1;
                }
                if (bounds.Length != 2 || (!hasStart && !hasEnd) || start < 0 || start >= size || end < start)
                {
                    ctx.Response.Headers["Content-Range"] = "bytes */" + size.ToString(CultureInfo.InvariantCulture);
                    await RouteHelper.SendErrorAsync(ctx, 416, "RangeNotSatisfiable", "The requested range is outside the asset.").ConfigureAwait(false);
                    return;
                }
                end = Math.Min(end, size - 1);
                byte[] part = new byte[end - start + 1];
                Array.Copy(bytes, start, part, 0, part.Length);
                ctx.Response.Headers["Content-Range"] = "bytes " + start.ToString(CultureInfo.InvariantCulture) + "-" + end.ToString(CultureInfo.InvariantCulture) + "/" + size.ToString(CultureInfo.InvariantCulture);
                bytes = part;
                status = 206;
            }
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "image/webp";
            await ctx.Response.Send(bytes, ctx.Token).ConfigureAwait(false);
        }

        private static string? Lower(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        }

        #endregion
    }
}
