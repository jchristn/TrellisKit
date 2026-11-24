namespace TrellisKit.Server.Services
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Security;
    using WatsonWebserver.Core;

    /// <summary>
    /// The live updates service graph of one property (docs/LIVE_UPDATES_CONTRACT.md section 24): signing, storage,
    /// the manifest endpoint, publishing, admin reads, and app configuration (flags, kill switches, content manifest).
    /// Also the deploy key authenticator and the API gate the authentication hook calls.
    /// Thread safety: every service is safe to share across requests.
    /// </summary>
    public class LiveUpdateServices
    {
        #region Public-Members

        /// <summary>
        /// Update storage.
        /// </summary>
        public UpdateObjectStore Store { get; }

        /// <summary>
        /// Code signing key ring and signer.
        /// </summary>
        public UpdateSigningService Signing { get; }

        /// <summary>
        /// Admin reads, channels, keys, adoption, and health.
        /// </summary>
        public UpdateAdminService Admin { get; }

        /// <summary>
        /// Uploads and lifecycle.
        /// </summary>
        public UpdatePublishService Publish { get; }

        /// <summary>
        /// The Expo Updates protocol endpoint, events, and assets.
        /// </summary>
        public UpdateManifestService Manifest { get; }

        /// <summary>
        /// Feature flags, kill switches, content versions, and the app manifest.
        /// </summary>
        public AppConfigService AppConfig { get; }

        /// <summary>
        /// Remote strings, banners, and remote assets.
        /// </summary>
        public AppContentService Content { get; }

        /// <summary>
        /// Launch failure listing and server-side source map symbolication.
        /// </summary>
        public UpdateSymbolicationService Symbolication { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Build the graph from a property's application services.
        /// </summary>
        /// <param name="services">Application services of the property.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public LiveUpdateServices(ApplicationServices services)
        {
            ArgumentNullException.ThrowIfNull(services);
            string propertyId = services.PropertyId;
            Store = new UpdateObjectStore(services.Settings);
            Signing = new UpdateSigningService(services.Database, services.Settings, services.Property.Property, services.Logger);
            Admin = new UpdateAdminService(services.Database, services.Settings, services.Enumeration, services.Metadata, Signing, Store, propertyId);
            Publish = new UpdatePublishService(services.Database, services.Settings, Admin, Signing, Store, services.Metadata, services.Audit, services.Logger, propertyId);
            Manifest = new UpdateManifestService(services.Database, services.Settings, Admin, Signing, Store, services.Limits, services.Logger, propertyId);
            AppConfig = new AppConfigService(services.Database, services.Settings, services.Property.Property, services.Enumeration, services.Metadata, services.Audit, services.Tokens, services.Limits);
            Content = new AppContentService(services.Database, services.Settings, services.Enumeration, services.Metadata, services.Audit, Store, services.Integrations.ImageProcessor, services.WordFilter, services.Limits, propertyId);
            Symbolication = new UpdateSymbolicationService(services.Database, services.Settings, Store, services.Enumeration, propertyId);
            AppConfig.Content = Content;
            AppConfig.InstallHash = Manifest.InstallHash;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Authenticate a deploy key bearer secret (tkdk_...).
        /// </summary>
        /// <param name="secret">Presented secret.</param>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Request context of the key, or null when invalid.</returns>
        public async Task<RequestContext?> AuthenticateDeployKeyAsync(string secret, HttpContextBase ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            UpdateDeployKey? key = await Admin.AuthenticateDeployKeyAsync(secret, ctx.Token).ConfigureAwait(false);
            if (key == null) return null;
            return new RequestContext
            {
                IsAuthenticated = true,
                IsAdmin = true,
                AgeAttested = true,
                HasMembership = true,
                PrincipalKind = TrellisKit.Core.Enums.PrincipalKindEnum.BreakGlass,
                PrincipalName = "deploy key " + key.Name,
                Scheme = "DeployKey",
                DeployKeyId = key.Id,
                DeployKeyChannels = key.Channels
            };
        }

        /// <summary>
        /// Enforce kill switches and the minimum binary version on one request. When refused, the response (503
        /// FeatureDisabled with Retry-After, or 426 ClientUpdateRequired) is sent.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="context">Authenticated caller, or null for anonymous routes.</param>
        /// <returns>True when the request was refused and answered.</returns>
        public async Task<bool> GateAsync(HttpContextBase ctx, RequestContext? context)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            AppGateDecision? decision = await AppConfig.GateAsync(ctx.Request.Method.ToString(), ctx.Request.Url.RawWithoutQuery, context, name => ctx.Request.RetrieveHeaderValue(name), ctx.Token).ConfigureAwait(false);
            if (decision == null) return false;
            if (decision.RetryAfterSeconds != null) ctx.Response.Headers["Retry-After"] = decision.RetryAfterSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ctx.Response.StatusCode = decision.StatusCode;
            ctx.Response.ContentType = "application/json";
            object body = decision.Error == "FeatureDisabled" ? new TrellisKit.Core.Responses.FeatureDisabledResponse(decision.Message, decision.Feature) : new TrellisKit.Core.Responses.ErrorResponse(decision.Error, decision.Message);
            await ctx.Response.Send(TrellisKit.Server.Serialization.ApiJson.Serialize(body), ctx.Token).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Whether a deploy key may call a route: upload, asset HEAD, publish of a group, and the health check.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Path without query.</param>
        /// <returns>True when allowed.</returns>
        public static bool DeployKeyPermits(string method, string? path)
        {
            string p = (path ?? String.Empty).TrimEnd('/');
            string prefix = TrellisKit.Core.Constants.ApiPrefix + "/admin/";
            if (method == "POST" && p == prefix + "updates") return true;
            if (method == "HEAD" && p.StartsWith(prefix + "updates/assets/", StringComparison.Ordinal)) return true;
            if (method == "GET" && p == prefix + "updates/health") return true;
            if (method == "POST" && p.StartsWith(prefix + "update-groups/", StringComparison.Ordinal) && p.EndsWith("/publish", StringComparison.Ordinal)) return true;
            return false;
        }

        #endregion
    }
}
