namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Anonymous live update routes used by the app (docs/LIVE_UPDATES_CONTRACT.md sections 3, 4.2, and 9.2): the Expo
    /// Updates protocol manifest endpoint, Local mode asset delivery, and client events. PreAuthentication.
    /// </summary>
    public class UpdateManifestRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/updates";
        private const string _Tag = "Live updates";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public UpdateManifestRoutes(PropertyDirectory directory)
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
            WatsonWebserver.Core.Routing.RoutingGroup pre = server.Routes.PreAuthentication;
            pre.Static.Add(HttpMethod.GET, _Base + "/manifest", ManifestAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Expo Updates manifest", _Tag, "Expo Updates protocol v1. Headers: expo-protocol-version, expo-platform, expo-runtime-version, expo-channel-name, expo-current-update-id, expo-embedded-update-id, " +
                    "expo-expect-signature, expo-recent-failed-update-ids, X-TK-Install-Id, X-Client-Version, X-TK-Property, X-TK-App. Returns multipart/mixed with a signed manifest (plus extensions and, after a key rotation, " +
                    "certificate_chain) or a signed directive (noUpdateAvailable, rollBackToEmbedded). Errors are text/plain: 400 MissingProtocolHeader, UnsupportedPlatform, UnknownChannel; 429 RateLimited; 503 SigningUnavailable. Anonymous.")
                    .File("multipart/mixed", "Signed manifest or directive").Resp(400, "MissingProtocolHeader, UnsupportedPlatform, or UnknownChannel").Resp(429, "RateLimited").Resp(503, "SigningUnavailable"));
            pre.Parameter.Add(HttpMethod.GET, _Base + "/assets/{propertyId}/{sha256Hex}", AssetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read an update asset", _Tag, "Content addressed and immutable (Cache-Control: public, max-age=31536000, immutable; ETag the SHA-256). Supports Range. Local mode only; in AWS the CDN serves the same path from the updates bucket. Anonymous.")
                    .PathParam("propertyId", "Property id").PathParam("sha256Hex", "SHA-256 hex of the bytes").File("application/octet-stream", "Asset bytes").Resp(206, "Partial content").Resp(304, "Not modified").Resp(404, "NotFound"));
            pre.Static.Add(HttpMethod.POST, _Base + "/events", EventsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Report update events", _Tag, "Body { events [{ type, updateId?, errorCode?, occurredUtc, launchDurationMs? }] }. X-TK-Install-Id required. 1 to 20 events: Downloaded, DownloadFailed, Applied, LaunchFailed, CheckFailed, RolledBackToEmbedded, CriticalPromptShown, CriticalRestarted. Events older than 7 days or more than 5 minutes ahead are dropped. Anonymous.")
                    .Body("UpdateEventsRequest").Returns("{ accepted, dropped }", 202).Resp(400, "ValidationFailed").Resp(429, "RateLimited"));
        }

        #endregion

        #region Private-Methods

        private async Task ManifestAsync(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            HttpRequestBase r = ctx.Request;
            UpdateManifestRequest request = new UpdateManifestRequest
            {
                ProtocolVersion = Int32.TryParse(r.RetrieveHeaderValue("expo-protocol-version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int protocol) ? protocol : 0,
                Platform = r.RetrieveHeaderValue("expo-platform"),
                RuntimeVersion = r.RetrieveHeaderValue("expo-runtime-version"),
                Channel = r.RetrieveHeaderValue("expo-channel-name"),
                CurrentUpdateId = r.RetrieveHeaderValue("expo-current-update-id"),
                EmbeddedUpdateId = r.RetrieveHeaderValue("expo-embedded-update-id"),
                ExpectSignature = r.RetrieveHeaderValue("expo-expect-signature"),
                RecentFailedUpdateIds = r.RetrieveHeaderValue("expo-recent-failed-update-ids"),
                ExtraParams = r.RetrieveHeaderValue("expo-extra-params"),
                InstallId = r.RetrieveHeaderValue("X-TK-Install-Id"),
                AppVersion = r.RetrieveHeaderValue("X-Client-Version"),
                Accept = r.RetrieveHeaderValue("Accept"),
                AssetOrigin = MediaRoutes.RequestOrigin(ctx, services.Settings.Rest.Ssl),
                SourceIp = ClientAddressResolver.Resolve(ctx, services.Settings.Rest.TrustedProxies)
            };
            UpdateManifestResult result = await services.LiveUpdates.Manifest.HandleAsync(request, ctx.Token).ConfigureAwait(false);
            foreach (KeyValuePair<string, string> header in result.Headers) ctx.Response.Headers[header.Key] = header.Value;
            ctx.Response.StatusCode = result.StatusCode;
            ctx.Response.ContentType = result.ContentType;
            if (result.Body.Length == 0) await ctx.Response.Send(ctx.Token).ConfigureAwait(false);
            else await ctx.Response.Send(result.Body, ctx.Token).ConfigureAwait(false);
        }

        private async Task AssetAsync(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            string sha = RouteHelper.Param(ctx, "sha256Hex");
            UpdateAsset? asset = await services.LiveUpdates.Manifest.FindAssetAsync(RouteHelper.Param(ctx, "propertyId"), sha, ctx.Token).ConfigureAwait(false);
            if (asset == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Asset not found.").ConfigureAwait(false);
                return;
            }
            string etag = "\"" + asset.Sha256Hex + "\"";
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
            long offset = 0;
            long length = asset.SizeBytes;
            int status = 200;
            string? range = ctx.Request.RetrieveHeaderValue("Range");
            if (!String.IsNullOrWhiteSpace(range) && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) && !range.Contains(','))
            {
                string[] bounds = range.Substring(6).Split('-');
                bool hasStart = Int64.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out long start);
                bool hasEnd = bounds.Length > 1 && Int64.TryParse(bounds[1], NumberStyles.None, CultureInfo.InvariantCulture, out long _);
                long end = hasEnd ? Int64.Parse(bounds[1], CultureInfo.InvariantCulture) : asset.SizeBytes - 1;
                if (!hasStart && hasEnd)
                {
                    start = Math.Max(0, asset.SizeBytes - end);
                    end = asset.SizeBytes - 1;
                }
                if (start < 0 || start >= asset.SizeBytes || end < start)
                {
                    ctx.Response.Headers["Content-Range"] = "bytes */" + asset.SizeBytes;
                    await RouteHelper.SendErrorAsync(ctx, 416, "RangeNotSatisfiable", "The requested range is outside the asset.").ConfigureAwait(false);
                    return;
                }
                end = Math.Min(end, asset.SizeBytes - 1);
                offset = start;
                length = end - start + 1;
                status = 206;
                ctx.Response.Headers["Content-Range"] = "bytes " + start + "-" + end + "/" + asset.SizeBytes;
            }
            byte[] bytes = length == 0 ? Array.Empty<byte>() : await services.LiveUpdates.Manifest.ReadAssetAsync(asset.Sha256Hex, offset, (int)Math.Min(length, Int32.MaxValue), ctx.Token).ConfigureAwait(false);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = asset.ContentType;
            await ctx.Response.Send(bytes, ctx.Token).ConfigureAwait(false);
        }

        private async Task EventsAsync(HttpContextBase ctx)
        {
            UpdateEventsRequest request = RouteHelper.ReadJson<UpdateEventsRequest>(ctx);
            Dictionary<string, int> result = await _Directory.For(ctx).LiveUpdates.Manifest.ReportEventsAsync(ctx.Request.RetrieveHeaderValue("X-TK-Install-Id"), request, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 202, result).ConfigureAwait(false);
        }

        #endregion
    }
}
