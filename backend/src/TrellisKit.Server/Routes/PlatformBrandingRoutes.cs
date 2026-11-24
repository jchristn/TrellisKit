namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.Routing;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Platform routes for any property's brand palette and AI switch (docs/TRELLISKIT.md sections 2.1 and 9.5): the
    /// same bodies as /api/v1/admin/property/branding and /api/v1/admin/property/ai. Platform administrator token or
    /// the break-glass token; every change is audited in the platform audit and in the property's audit.
    /// </summary>
    public class PlatformBrandingRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/platform/properties/{propertyId}";
        private const string _Tag = "Platform";
        private const string _Auth = " Platform administrator token (Authorization: Bearer tkp_...) or the break-glass admin token; platform host only.";
        private readonly PlatformService _Platform;
        private readonly SettingsService _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="platform">Platform service (authentication, property directory, platform audit).</param>
        /// <param name="settings">Settings service (client address resolution).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PlatformBrandingRoutes(PlatformService platform, SettingsService settings)
        {
            _Platform = platform ?? throw new ArgumentNullException(nameof(platform));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
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
            RoutingGroup g = server.Routes.PreAuthentication;
            g.Parameter.Add(HttpMethod.GET, _Base + "/branding", GetBrandingAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a property's brand palette", _Tag, "Same body as GET /api/v1/admin/property/branding, for any property." + _Auth)
                    .PathParam("propertyId", "Property id").Bounded(PropertyBrandingRoutes.BrandingShape, PropertyBrandingRoutes.BrandingBound).Resp(401, "Unauthorized").Resp(404, "NotFound"));
            g.Parameter.Add(HttpMethod.PUT, _Base + "/branding", PutBrandingAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Change a property's brand palette", _Tag, "Same as PUT /api/v1/admin/property/branding, for any property. " + PropertyBrandingRoutes.UpdateDescription + " Also audited in the platform audit." + _Auth)
                    .PathParam("propertyId", "Property id").Body("BrandingUpdateRequest").Bounded(PropertyBrandingRoutes.BrandingShape, PropertyBrandingRoutes.BrandingBound).Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(409, "Conflict"));
            g.Parameter.Add(HttpMethod.POST, _Base + "/branding/revert", RevertBrandingAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Revert a property's brand palette", _Tag, "Same as POST /api/v1/admin/property/branding/revert, for any property. " + PropertyBrandingRoutes.RevertDescription + " Also audited in the platform audit." + _Auth)
                    .PathParam("propertyId", "Property id").Body("BrandingRevertRequest").Bounded(PropertyBrandingRoutes.BrandingShape, PropertyBrandingRoutes.BrandingBound).Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(409, "NoPreviousVersion"));
            g.Parameter.Add(HttpMethod.PUT, _Base + "/branding/member-presets", PutMemberPresetsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Choose the presets a property's members may pick", _Tag, "Same as PUT /api/v1/admin/property/branding/member-presets, for any property. " + PropertyBrandingRoutes.MemberPresetsDescription + " Also audited in the platform audit." + _Auth)
                    .PathParam("propertyId", "Property id").Body("MemberPresetsRequest").Bounded(PropertyBrandingRoutes.BrandingShape, PropertyBrandingRoutes.BrandingBound).Resp(400, "ValidationFailed").Resp(404, "NotFound"));
            EnumerationRoutes.Add(g, _Base + "/branding/versions", BrandingCollections.Versions, "BrandingVersion",
                () => Doc("List a property's brand palette versions", _Tag, "Same as GET /api/v1/admin/property/branding/versions, for any property. " + PropertyBrandingRoutes.VersionsDescription + _Auth).Resp(401, "Unauthorized").Resp(404, "NotFound"),
                async (ctx, query) =>
                {
                    await CallerAsync(ctx).ConfigureAwait(false);
                    return await RuntimeOf(ctx).Services.Branding.VersionsAsync(query, ctx.Token).ConfigureAwait(false);
                }, "propertyId");
            g.Parameter.Add(HttpMethod.GET, _Base + "/ai", GetAiAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a property's AI switch", _Tag, "Same body as GET /api/v1/admin/property/ai, for any property." + _Auth)
                    .PathParam("propertyId", "Property id").Returns(PropertyBrandingRoutes.AiShape).Resp(401, "Unauthorized").Resp(404, "NotFound"));
            g.Parameter.Add(HttpMethod.PUT, _Base + "/ai", PutAiAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Switch AI on or off for a property", _Tag, "Same as PUT /api/v1/admin/property/ai, for any property. " + PropertyBrandingRoutes.AiUpdateDescription + " Also audited in the platform audit." + _Auth)
                    .PathParam("propertyId", "Property id").Body("PropertyAiRequest").Returns(PropertyBrandingRoutes.AiShape).Resp(400, "ValidationFailed").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private async Task<PlatformCaller> CallerAsync(HttpContextBase ctx)
        {
            PlatformCaller? caller = await _Platform.AuthenticateAsync(ctx.Request.RetrieveHeaderValue("Authorization"), ClientAddressResolver.Resolve(ctx, _Settings.Rest), ctx.Token).ConfigureAwait(false);
            if (caller == null) throw new ApiErrorException(401, "Unauthorized", "A valid platform administrator token is required.");
            return caller;
        }

        private PropertyRuntime RuntimeOf(HttpContextBase ctx)
        {
            PropertyRuntime? runtime = _Platform.Directory.Find(RouteHelper.Param(ctx, "propertyId"));
            if (runtime == null) throw ApiErrorException.NotFound("Property");
            return runtime;
        }

        private static RequestInfo InfoOf(PlatformCaller caller, PropertyRuntime runtime)
        {
            return new RequestInfo { SourceIp = caller.SourceIp, Context = new RequestContext { PropertyId = runtime.Id, UserId = caller.ActorId, PrincipalName = caller.ActorName } };
        }

        private async Task GetBrandingAsync(HttpContextBase ctx)
        {
            await CallerAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await RuntimeOf(ctx).Services.Branding.ViewAsync(ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PutBrandingAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            PropertyRuntime runtime = RuntimeOf(ctx);
            BrandingUpdateRequest request = RouteHelper.ReadJson<BrandingUpdateRequest>(ctx);
            BrandingView view = await runtime.Services.Branding.UpdateAsync(InfoOf(caller, runtime), request, ctx.Token).ConfigureAwait(false);
            await _Platform.AuditAsync(caller, "property.branding.update", "Property", runtime.Id, "version " + view.Version + (view.Overrides.Count > 0 ? "; warning: " + view.Overrides.Count + " contrast override(s)" : String.Empty), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task RevertBrandingAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            PropertyRuntime runtime = RuntimeOf(ctx);
            BrandingRevertRequest request = RouteHelper.ReadJson<BrandingRevertRequest>(ctx);
            BrandingView view = await runtime.Services.Branding.RevertAsync(InfoOf(caller, runtime), request, ctx.Token).ConfigureAwait(false);
            await _Platform.AuditAsync(caller, "property.branding.revert", "Property", runtime.Id, "version " + view.Version + " (" + view.Source + ")", ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task PutMemberPresetsAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            PropertyRuntime runtime = RuntimeOf(ctx);
            MemberPresetsRequest request = RouteHelper.ReadJson<MemberPresetsRequest>(ctx);
            BrandingView view = await runtime.Services.Branding.UpdateMemberPresetsAsync(InfoOf(caller, runtime), request, ctx.Token).ConfigureAwait(false);
            await _Platform.AuditAsync(caller, "property.branding.memberPresets", "Property", runtime.Id, "mode " + view.MemberPresets.Mode + " (" + view.MemberPresets.AllowedPresetIds.Count + " presets allowed)", ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task GetAiAsync(HttpContextBase ctx)
        {
            await CallerAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, RuntimeOf(ctx).Services.AiSwitch.View()).ConfigureAwait(false);
        }

        private async Task PutAiAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            PropertyRuntime runtime = RuntimeOf(ctx);
            PropertyAiRequest request = RouteHelper.ReadJson<PropertyAiRequest>(ctx);
            bool before = runtime.Services.AiSwitch.Enabled;
            PropertyAiView view = await runtime.Services.AiSwitch.UpdateAsync(InfoOf(caller, runtime), request, ctx.Token).ConfigureAwait(false);
            if (before != view.Enabled) await _Platform.AuditAsync(caller, "property.ai.update", "Property", runtime.Id, "enabled " + before + " -> " + view.Enabled, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        #endregion
    }
}
