namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.Routing;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Property document routes (docs/TRELLISKIT.md section 14): the public document read, the member's acceptance and
    /// acceptance history, and the admin console's documents, versions, publishing, and acceptances.
    /// </summary>
    public class DocumentRoutes
    {
        #region Public-Members

        /// <summary>
        /// Shape of a document summary.
        /// </summary>
        public const string SummaryShape = "DocumentSummary { key, title, kind, acceptance, separateCheckbox, reacceptOnChange, slug, footer, description, version, sequence, updatedUtc, labels, tags }";

        /// <summary>
        /// Shape of an admin document.
        /// </summary>
        public const string AdminShape = "AdminDocument { DocumentSummary fields, source, versionId, versionCount, acceptedCurrentCount, acceptedAnyCount, requiredSequence, syncsFromFolder, createdByUserId, changeSummary, content (single reads) }";

        /// <summary>
        /// Shape of a document version.
        /// </summary>
        public const string VersionShape = "DocumentVersion { id, documentKey, version, sequence, title, changeSummary, requiresReacceptance, source, contentLength, createdByUserId, createdUtc, acceptanceCount, content (single reads), labels, tags }";

        /// <summary>
        /// Shape of a document acceptance.
        /// </summary>
        public const string AcceptanceShape = "DocumentAcceptance { id, documentKey, documentTitle, version, sequence, versionId, userId, user: UserSummary (staff), method: Signup | Reacceptance, client: Web | Ios | Android | Other, acceptedUtc, createdUtc, labels, tags }";

        /// <summary>
        /// Bound of the admin document list.
        /// </summary>
        public const string ListBound = "the documents declared in property.json (at most 30)";

        /// <summary>
        /// Description of the publish body.
        /// </summary>
        public const string PublishDescription = "Body { version (1 to 32 characters, new for the document), content (Markdown, 1 to property.maxDocumentCharacters), changeSummary (1 to 500), title? (1 to 120), requireReacceptance? (gating documents only; default the document's reacceptOnChange) }. " +
            "Stores a new current version (source Edit); with requireReacceptance, signed-in members who accepted an earlier version get 403 DocumentAcceptanceRequired until they accept it. In SingleProperty a later change to the document on disk replaces it again (the folder wins on change). Audited document.publish.";

        #endregion

        #region Private-Members

        private const string _Tag = "Documents";
        private const string _AdminTag = "Admin documents";
        private const string _Public = Constants.ApiPrefix + "/public/documents";
        private const string _Me = Constants.ApiPrefix + "/me/documents";
        private const string _Admin = Constants.ApiPrefix + "/admin/documents";
        private const string _AdminAuth = " SystemAdmin, the admin token, or a delegated platform session.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public DocumentRoutes(PropertyDirectory directory)
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
            RoutingGroup pre = server.Routes.PreAuthentication;
            RoutingGroup post = server.Routes.PostAuthentication;

            pre.Parameter.Add(HttpMethod.GET, _Public + "/{key}", PublicGetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a property document", _Tag, "The current version of a document the property declares, with its Markdown content (live: an admin publish shows at once). Cache-Control: public, max-age=60. Anonymous.")
                    .PathParam("key", "Document key").Returns("Document { DocumentSummary fields, content, changeSummary }").Resp(404, "NotFound"));

            post.Static.Add(HttpMethod.POST, _Me + "/accept", AcceptAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Accept documents", _Tag, "Body { acceptTerms?, documents: [{ key, version }] }: records the member's acceptance of each listed document at its current version (acceptTerms adds every document the sign-up terms checkbox covers). " +
                    "Method Reacceptance when the member had accepted an earlier version, otherwise Signup; client from X-Client and X-Client-Platform. Clears 403 DocumentAcceptanceRequired. Audited document.accept. Signed in.")
                    .Body("DocumentAcceptRequest").Returns("Me (pendingDocuments lists what is still pending)").Resp(400, "ValidationFailed (fieldErrors documents.<key> for unknown keys and stale versions)"));
            EnumerationRoutes.Add(post, _Me + "/acceptances", DocumentCollections.MyAcceptances, AcceptanceShape,
                () => Doc("List my document acceptances", _Tag, "The caller's own acceptances, newest first (orderings AcceptedDescending, AcceptedAscending, CreatedDescending, CreatedAscending; filter documentKey). Signed in."),
                async (ctx, query) => await _Directory.For(ctx).Documents.EnumerateAcceptancesAsync(query, RouteHelper.RequireUserId(ctx), ctx.Token).ConfigureAwait(false));

            EnumerationRoutes.Add(post, _Admin, AdministrativeCatalogs.Documents, "AdminDocumentView",
                () => Doc("Enumerate property documents", _Tag, "Declared documents with standard paging, creation window on the current publication time, and syncsFromFolder context."),
                async (ctx, query) =>
                {
                    ApplicationServices services = _Directory.For(ctx);
                    EnumerationRequest request = services.Enumeration.Prepare(query, AdministrativeCatalogs.Documents, true);
                    AdminDocumentListView result = await services.Documents.AdminListAsync(ctx.Token).ConfigureAwait(false);
                    EnumerationResult<AdminDocumentView> page = CatalogEnumeration.Page(request, result.Objects, item => item.Key, item => item.UpdatedUtc, item => item.Key + " " + item.Title, item => item.Labels, item => item.Tags);
                    return CatalogEnumerationResponse<AdminDocumentView>.From(page, new Dictionary<string, object?> { ["syncsFromFolder"] = result.SyncsFromFolder });
                });
            EnumerationRoutes.Add(post, _Admin + "/acceptances", DocumentCollections.Acceptances, AcceptanceShape,
                () => Doc("List document acceptances", _AdminTag, "Acceptances across documents (orderings AcceptedDescending, the default, AcceptedAscending, CreatedDescending, CreatedAscending; filters documentKey, userId, version, method, client; search covers the member's display name and email and the document key). The member drawer filters by userId." + _AdminAuth),
                async (ctx, query) => await _Directory.For(ctx).Documents.EnumerateAcceptancesAsync(query, null, ctx.Token).ConfigureAwait(false));
            post.Parameter.Add(HttpMethod.GET, _Admin + "/{key}", AdminGetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a property document", _AdminTag, "One document with its current content and counts." + _AdminAuth)
                    .PathParam("key", "Document key").Returns(AdminShape).Resp(403, "Forbidden").Resp(404, "NotFound"));
            EnumerationRoutes.Add(post, _Admin + "/{key}/versions", DocumentCollections.Versions, VersionShape,
                () => Doc("List a document's versions", _AdminTag, "Every stored version, newest first (orderings SequenceDescending, the default, CreatedDescending, CreatedAscending; search covers version, title, and change summary); content omitted." + _AdminAuth),
                async (ctx, query) => await _Directory.For(ctx).Documents.EnumerateVersionsAsync(RouteHelper.Param(ctx, "key"), query, ctx.Token).ConfigureAwait(false), "key");
            post.Parameter.Add(HttpMethod.POST, _Admin + "/{key}/versions", PublishAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Publish a document version", _AdminTag, PublishDescription + _AdminAuth)
                    .PathParam("key", "Document key").Body("DocumentPublishRequest").Returns(AdminShape, 201).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "VersionExists or Conflict"));
            post.Parameter.Add(HttpMethod.GET, _Admin + "/{key}/versions/{versionId}", AdminVersionAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a document version", _AdminTag, "One version with its content and acceptance count." + _AdminAuth)
                    .PathParam("key", "Document key").PathParam("versionId", "Version identifier (dcv_)").Returns(VersionShape).Resp(403, "Forbidden").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private async Task PublicGetAsync(HttpContextBase ctx)
        {
            DocumentView view = await _Directory.For(ctx).Documents.GetPublicAsync(RouteHelper.Param(ctx, "key"), ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["Cache-Control"] = "public, max-age=60";
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task AcceptAsync(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            string userId = RouteHelper.RequireUserId(ctx);
            DocumentAcceptRequest request = RouteHelper.ReadJson<DocumentAcceptRequest>(ctx);
            await services.Documents.AcceptAsync(userId, request, RequestInfo.From(ctx, services.Settings.Rest), ctx.Token).ConfigureAwait(false);
            User? user = await services.Database.Users.ReadAsync(userId, ctx.Token).ConfigureAwait(false);
            if (user == null) throw ApiErrorException.NotFound("User");
            await RouteHelper.SendJsonAsync(ctx, 200, await services.Views.BuildMeAsync(user, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task AdminListAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Documents.AdminListAsync(ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task AdminGetAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Documents.AdminGetAsync(RouteHelper.Param(ctx, "key"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task AdminVersionAsync(HttpContextBase ctx)
        {
            DocumentVersionView view = await _Directory.For(ctx).Documents.GetVersionAsync(RouteHelper.Param(ctx, "key"), RouteHelper.Param(ctx, "versionId"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task PublishAsync(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            DocumentPublishRequest request = RouteHelper.ReadJson<DocumentPublishRequest>(ctx);
            AdminDocumentView view = await services.Documents.PublishAsync(RequestInfo.From(ctx, services.Settings.Rest), RouteHelper.Param(ctx, "key"), request, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, view).ConfigureAwait(false);
        }

        #endregion
    }
}
