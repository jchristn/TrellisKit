namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Generic label and tag routes and the discovery enumerations (docs/ENUMERATION_AND_METADATA.md section 1).
    /// Member group, except the admin bulk route (Admin group).
    /// </summary>
    public class MetadataRoutes
    {
        #region Private-Members

        private const string _Tag = "Labels and tags";
        private const string _Discovery = "Most used first; staff: entries and objects the caller cannot see are never counted for non-staff.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public MetadataRoutes(PropertyDirectory directory)
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
            string item = Constants.ApiPrefix + "/metadata/{targetType}/{targetId}";
            post.Parameter.Add(HttpMethod.GET, item, GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read labels and tags", _Tag, "The owner, staff, or (for users, feed posts, comments, attached media, and reactions) any member. Staff entries are only shown to staff.")
                    .PathParam("targetType", "Object type, for example FeedPost").PathParam("targetId", "Object id").Returns("Metadata").Resp(400, "ValidationFailed").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, item, PutAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Replace labels and tags", _Tag, "Owner or staff. A provided labels list or tags dictionary replaces the stored one; omitted means unchanged. Non-staff staff: entries are ignored.")
                    .PathParam("targetType", "Object type").PathParam("targetId", "Object id").Body("{ labels?, tags? }").Returns("Metadata").Resp(400, "ValidationFailed or ContentFiltered").Resp(403, "Forbidden or Muted").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PATCH, item, PatchAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Change labels and tags", _Tag, "Owner or staff. Incremental change.")
                    .PathParam("targetType", "Object type").PathParam("targetId", "Object id").Body("{ addLabels?, removeLabels?, setTags?, removeTagKeys? }").Returns("Metadata").Resp(400, "ValidationFailed or ContentFiltered").Resp(403, "Forbidden or Muted").Resp(404, "NotFound"));
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/admin/metadata/bulk", BulkAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Bulk label and tag change", "Admin: labels and tags", "SystemAdmin or the admin token. Up to 500 objects of one type.")
                    .Body("{ targetType, targetIds, addLabels?, removeLabels?, setTags?, removeTagKeys? }").Returns("MetadataBulkResult").Resp(400, "ValidationFailed").Resp(403, "Forbidden"));
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/labels/enumerate", LabelsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate labels", _Tag, _Discovery).Body("EnumerationQuery plus targetType?, prefix?").Enumeration("LabelCount", "CountDescending", withQueryParameters: false).Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/tags/keys/enumerate", TagKeysAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate tag keys", _Tag, _Discovery).Body("EnumerationQuery plus targetType?, prefix?").Enumeration("TagKeyCount", "CountDescending", withQueryParameters: false).Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/tags/values/enumerate", TagValuesAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate tag values", _Tag, _Discovery).Body("EnumerationQuery plus key, targetType?, prefix?").Enumeration("TagValueCount", "CountDescending", withQueryParameters: false).Resp(400, "ValidationFailed"));
        }

        #endregion

        #region Private-Methods

        private async Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            return await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Metadata.ReadAsync(actor, RouteHelper.Param(ctx, "targetType"), RouteHelper.Param(ctx, "targetId"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PutAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            MetadataReplaceRequest request = RouteHelper.ReadJson<MetadataReplaceRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Metadata.ReplaceAsync(actor, RouteHelper.Param(ctx, "targetType"), RouteHelper.Param(ctx, "targetId"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PatchAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            MetadataPatchRequest request = RouteHelper.ReadJson<MetadataPatchRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Metadata.PatchAsync(actor, RouteHelper.Param(ctx, "targetType"), RouteHelper.Param(ctx, "targetId"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task BulkAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            MetadataBulkRequest request = RouteHelper.ReadJson<MetadataBulkRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Metadata.BulkAsync(actor, request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task LabelsAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            MetadataDiscoveryQuery query = RouteHelper.ReadEnumerationBody<MetadataDiscoveryQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Metadata.EnumerateLabelsAsync(actor, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task TagKeysAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            MetadataDiscoveryQuery query = RouteHelper.ReadEnumerationBody<MetadataDiscoveryQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Metadata.EnumerateTagKeysAsync(actor, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task TagValuesAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            MetadataDiscoveryQuery query = RouteHelper.ReadEnumerationBody<MetadataDiscoveryQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Metadata.EnumerateTagValuesAsync(actor, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
