namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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
    /// Platform routes for any property's documents (docs/TRELLISKIT.md section 14): the same shapes as
    /// /api/v1/admin/documents. Platform administrator token or the break-glass token; a publish is audited in the
    /// platform audit and in the property's audit.
    /// </summary>
    public class PlatformDocumentRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/platform/properties/{propertyId}/documents";
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
        public PlatformDocumentRoutes(PlatformService platform, SettingsService settings)
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
            EnumerationRoutes.Add(g, _Base, AdministrativeCatalogs.Documents, "AdminDocumentView",
                () => Doc("Enumerate property documents", _Tag, "Declared documents with standard paging, creation window on the current publication time, and syncsFromFolder context."),
                async (ctx, query) =>
                {
                    await CallerAsync(ctx).ConfigureAwait(false);
                    ApplicationServices services = RuntimeOf(ctx).Services;
                    EnumerationRequest request = services.Enumeration.Prepare(query, AdministrativeCatalogs.Documents, true);
                    AdminDocumentListView result = await services.Documents.AdminListAsync(ctx.Token).ConfigureAwait(false);
                    EnumerationResult<AdminDocumentView> page = CatalogEnumeration.Page(request, result.Objects, item => item.Key, item => item.UpdatedUtc, item => item.Key + " " + item.Title, item => item.Labels, item => item.Tags);
                    return CatalogEnumerationResponse<AdminDocumentView>.From(page, new Dictionary<string, object?> { ["syncsFromFolder"] = result.SyncsFromFolder });
                }, "propertyId");
            EnumerationRoutes.Add(g, _Base + "/acceptances", DocumentCollections.Acceptances, DocumentRoutes.AcceptanceShape,
                () => Doc("List a property's document acceptances", _Tag, "Same as /api/v1/admin/documents/acceptances, for any property." + _Auth),
                async (ctx, query) =>
                {
                    await CallerAsync(ctx).ConfigureAwait(false);
                    return await RuntimeOf(ctx).Services.Documents.EnumerateAcceptancesAsync(query, null, ctx.Token).ConfigureAwait(false);
                }, "propertyId");
            g.Parameter.Add(HttpMethod.GET, _Base + "/{key}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a property's document", _Tag, "Same as GET /api/v1/admin/documents/{key}, for any property." + _Auth)
                    .PathParam("propertyId", "Property id").PathParam("key", "Document key").Returns(DocumentRoutes.AdminShape).Resp(401, "Unauthorized").Resp(404, "NotFound"));
            EnumerationRoutes.Add(g, _Base + "/{key}/versions", DocumentCollections.Versions, DocumentRoutes.VersionShape,
                () => Doc("List a property document's versions", _Tag, "Same as /api/v1/admin/documents/{key}/versions, for any property." + _Auth),
                async (ctx, query) =>
                {
                    await CallerAsync(ctx).ConfigureAwait(false);
                    return await RuntimeOf(ctx).Services.Documents.EnumerateVersionsAsync(RouteHelper.Param(ctx, "key"), query, ctx.Token).ConfigureAwait(false);
                }, "propertyId", "key");
            g.Parameter.Add(HttpMethod.POST, _Base + "/{key}/versions", PublishAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Publish a property document version", _Tag, "Same as POST /api/v1/admin/documents/{key}/versions, for any property. " + DocumentRoutes.PublishDescription + _Auth)
                    .PathParam("propertyId", "Property id").PathParam("key", "Document key").Body("DocumentPublishRequest").Returns(DocumentRoutes.AdminShape, 201)
                    .Resp(400, "ValidationFailed").Resp(401, "Unauthorized").Resp(404, "NotFound").Resp(409, "VersionExists or Conflict"));
            g.Parameter.Add(HttpMethod.GET, _Base + "/{key}/versions/{versionId}", VersionAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a property document version", _Tag, "Same as GET /api/v1/admin/documents/{key}/versions/{versionId}, for any property." + _Auth)
                    .PathParam("propertyId", "Property id").PathParam("key", "Document key").PathParam("versionId", "Version identifier (dcv_)").Returns(DocumentRoutes.VersionShape).Resp(401, "Unauthorized").Resp(404, "NotFound"));
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

        private async Task ListAsync(HttpContextBase ctx)
        {
            await CallerAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await RuntimeOf(ctx).Services.Documents.AdminListAsync(ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            await CallerAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await RuntimeOf(ctx).Services.Documents.AdminGetAsync(RouteHelper.Param(ctx, "key"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task VersionAsync(HttpContextBase ctx)
        {
            await CallerAsync(ctx).ConfigureAwait(false);
            DocumentVersionView view = await RuntimeOf(ctx).Services.Documents.GetVersionAsync(RouteHelper.Param(ctx, "key"), RouteHelper.Param(ctx, "versionId"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task PublishAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            PropertyRuntime runtime = RuntimeOf(ctx);
            DocumentPublishRequest request = RouteHelper.ReadJson<DocumentPublishRequest>(ctx);
            AdminDocumentView view = await runtime.Services.Documents.PublishAsync(InfoOf(caller, runtime), RouteHelper.Param(ctx, "key"), request, ctx.Token).ConfigureAwait(false);
            await _Platform.AuditAsync(caller, "document.publish", "PropertyDocument", runtime.Id, view.Key + " version " + view.Version + " (sequence " + view.Sequence + ")", ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, view).ConfigureAwait(false);
        }

        #endregion
    }
}
