namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// The property's brand palette and AI switch in the admin portal (docs/TRELLISKIT.md sections 2.1 and 9.5): read,
    /// edit, and revert the palette; read and change the AI switch. SystemAdmin, the admin token, or a delegated
    /// platform session, in both deployment modes.
    /// </summary>
    public class PropertyBrandingRoutes
    {
        #region Public-Members

        /// <summary>
        /// Description of the branding view, shared with the platform routes.
        /// </summary>
        public const string BrandingShape = "BrandingView { propertyId, version, source, note, light, dark, keys, backgrounds, contrast, overrides, seed, previous, presets, memberPresets, syncsFromFolder, createdByUserId, createdUtc }";

        /// <summary>
        /// Bound of the branding view, shared with the platform routes.
        /// </summary>
        public const string BrandingBound = "8 color keys, 7 contrast pairs per scheme, at most one override per scheme and pair, and the engine palette preset catalog (at most 32)";

        /// <summary>
        /// Description of the AI switch view, shared with the platform routes.
        /// </summary>
        public const string AiShape = "PropertyAiView";

        /// <summary>
        /// Description of the branding GET route, shared with the platform routes.
        /// </summary>
        public const string ReadDescription = "The current brand palette; carries { version, source (Seed, Edit, RevertPrevious, RevertSeed, DirectorySync), light, dark (every key: primary, primaryHover, onPrimary, accent, onAccent, accentSoft, onAccentSoft, themeColor), " +
            "contrast [{ scheme, pair, foregroundColor, backgroundColor, ratio, required, level (Text, LargeText, Component), passes, overridden, usage }], overrides, seed (the property.json palette), previous (the version before, or null), syncsFromFolder }.";

        /// <summary>
        /// Description of the branding PUT route, shared with the platform routes.
        /// </summary>
        public const string UpdateDescription = "Body { light? (map of color key to #RRGGBB), dark? (same), contrastOverrides?: [{ scheme, pair, reason? }], note? }; keys left out keep their value. Every value must be #RRGGBB or #RGB. " +
            "Every pair must meet WCAG 2.1 AA (4.5:1 normal text, 3:1 components): a failing pair is 400 ValidationFailed with fieldErrors[\"contrast.<scheme>.<pair>\"] naming the colors and the ratio, unless contrastOverrides lists it, in which case the version is saved and a warning is audited as property.branding.contrastOverride. " +
            "Stores a new version (audited property.branding.update) and applies to GET /api/v1/property immediately (its ETag changes); a body that changes nothing stores nothing. In SingleProperty a later change of property.json brand.colors replaces the palette on the next start.";

        /// <summary>
        /// Description of the branding revert route, shared with the platform routes.
        /// </summary>
        public const string RevertDescription = "Body { to: Previous | Seed | Version, version?, note? }: stores the version before the current one (with its overrides), the retained version named by version (to: Version; source RevertVersion), or the property.json seed as a new version. Audited property.branding.revert (pairs below AA are listed as a warning). 404 NotFound for a version that is not retained; 409 AlreadyCurrent for the current version.";

        /// <summary>
        /// Description of the palette versions list (shared with the platform route).
        /// </summary>
        public const string VersionsDescription = "Retained palette versions (at most property.maxPaletteVersions), newest first: BrandingVersion { version, source, note, light, dark, failingPairs, overrideCount, isCurrent, createdByUserId, createdUtc, labels, tags }. Filter source; orderings VersionDescending (default), VersionAscending, CreatedDescending, CreatedAscending. Versions carry no labels or tags (always empty lists), so label and tag filters are 400 ValidationFailed. Restore one with POST .../branding/revert { to: Version, version }.";

        /// <summary>
        /// Description of the member presets PUT route, shared with the platform routes.
        /// </summary>
        public const string MemberPresetsDescription = "Body { mode: All | None | Selected, presetIds? }: which engine palette presets (BrandingView presets[].id) members may choose in Settings, Appearance; presetIds lists at least one catalog id with Selected and is ignored otherwise. " +
            "The property's brand palette is always the members' default option. A member whose preset is no longer allowed sees the brand palette (the preference is kept). Applies to GET /api/v1/property brand.presets immediately (its ETag changes). Audited property.branding.memberPresets; a body that changes nothing stores nothing. Returns the BrandingView.";

        /// <summary>
        /// Description of the AI GET route, shared with the platform routes.
        /// </summary>
        public const string AiReadDescription = "The property's AI switch; carries { enabled, deploymentEnabled (ai.enabled), available (both; GET /api/v1/property features.ai), seedEnabled (property.json features.ai), syncsFromFolder, revision, updatedByUserId, updatedUtc }.";

        /// <summary>
        /// Description of the AI PUT route, shared with the platform routes.
        /// </summary>
        public const string AiUpdateDescription = "Body { enabled }. Applies to the next request without a restart (other instances within deployment.propertyCacheSeconds) and changes the GET /api/v1/property ETag. Turning AI off keeps endpoints, assistants, and conversations; AI routes answer 404 NotAvailable until it is on. Audited property.ai.update.";

        #endregion

        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/property";
        private const string _Tag = "Admin: property";
        private const string _Admin = " SystemAdmin, the admin token, or a delegated platform session.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public PropertyBrandingRoutes(PropertyDirectory directory)
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
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;
            post.Static.Add(HttpMethod.GET, _Base + "/branding", GetBrandingAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read the brand palette", _Tag, ReadDescription + _Admin).Bounded(BrandingShape, BrandingBound));
            post.Static.Add(HttpMethod.PUT, _Base + "/branding", PutBrandingAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Change the brand palette", _Tag, UpdateDescription + _Admin)
                    .Body("BrandingUpdateRequest").Bounded(BrandingShape, BrandingBound).Resp(400, "ValidationFailed").Resp(409, "Conflict"));
            post.Static.Add(HttpMethod.POST, _Base + "/branding/revert", RevertBrandingAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Revert the brand palette", _Tag, RevertDescription + _Admin)
                    .Body("BrandingRevertRequest").Bounded(BrandingShape, BrandingBound).Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(409, "NoPreviousVersion or AlreadyCurrent"));
            post.Static.Add(HttpMethod.PUT, _Base + "/branding/member-presets", PutMemberPresetsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Choose the presets members may pick", _Tag, MemberPresetsDescription + _Admin)
                    .Body("MemberPresetsRequest").Bounded(BrandingShape, BrandingBound).Resp(400, "ValidationFailed"));
            EnumerationRoutes.Add(post, _Base + "/branding/versions", BrandingCollections.Versions, "BrandingVersion",
                () => Doc("List brand palette versions", _Tag, VersionsDescription + _Admin),
                async (ctx, query) => await _Directory.For(ctx).Branding.VersionsAsync(query, ctx.Token).ConfigureAwait(false));
            post.Static.Add(HttpMethod.GET, _Base + "/ai", GetAiAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read the AI switch", _Tag, AiReadDescription + _Admin).Returns(AiShape));
            post.Static.Add(HttpMethod.PUT, _Base + "/ai", PutAiAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Switch AI on or off", _Tag, AiUpdateDescription + _Admin).Body("PropertyAiRequest").Returns(AiShape).Resp(400, "ValidationFailed"));
        }

        #endregion

        #region Private-Methods

        private async Task GetBrandingAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Branding.ViewAsync(ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PutBrandingAsync(HttpContextBase ctx)
        {
            BrandingUpdateRequest request = RouteHelper.ReadJson<BrandingUpdateRequest>(ctx);
            ApplicationServices services = _Directory.For(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await services.Branding.UpdateAsync(RequestInfo.From(ctx, services.Settings.Rest), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RevertBrandingAsync(HttpContextBase ctx)
        {
            BrandingRevertRequest request = RouteHelper.ReadJson<BrandingRevertRequest>(ctx);
            ApplicationServices services = _Directory.For(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await services.Branding.RevertAsync(RequestInfo.From(ctx, services.Settings.Rest), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PutMemberPresetsAsync(HttpContextBase ctx)
        {
            MemberPresetsRequest request = RouteHelper.ReadJson<MemberPresetsRequest>(ctx);
            ApplicationServices services = _Directory.For(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await services.Branding.UpdateMemberPresetsAsync(RequestInfo.From(ctx, services.Settings.Rest), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAiAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, _Directory.For(ctx).AiSwitch.View()).ConfigureAwait(false);
        }

        private async Task PutAiAsync(HttpContextBase ctx)
        {
            PropertyAiRequest request = RouteHelper.ReadJson<PropertyAiRequest>(ctx);
            ApplicationServices services = _Directory.For(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await services.AiSwitch.UpdateAsync(RequestInfo.From(ctx, services.Settings.Rest), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
