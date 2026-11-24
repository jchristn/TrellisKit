namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin media library (contract section 10.3, enumeration per the enumeration and metadata specification).
    /// Admin group (SystemAdmin or the break-glass token).
    /// </summary>
    public class AdminMediaRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/media";
        private const string _Tag = "Admin media";
        private const string _Admin = " Admin.";
        private const string _Filters = "kind, status, ownerUserId, attached";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminMediaRoutes(PropertyDirectory directory)
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
            string orderings = MediaCollections.AdminLibrary.OrderingNames();
            post.Static.Add(HttpMethod.POST, _Base + "/enumerate", EnumerateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate the media library", _Tag, "EnumerationQuery body; search over file name and alt text; filters " + _Filters + "." + _Admin)
                    .Body("EnumerationQuery").Enumeration("AdminMediaItem", orderings, _Filters, false).Resp(400, "ValidationFailed or InvalidContinuationToken"));
            post.Static.Add(HttpMethod.GET, _Base, ListAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List the media library", _Tag, "GET form of the enumeration." + _Admin)
                    .Enumeration("AdminMediaItem", orderings, _Filters).Resp(400, "ValidationFailed or InvalidContinuationToken"));
            post.Static.Add(HttpMethod.GET, _Base + "/summary", SummaryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Media library totals", _Tag, "{ totalCount, totalStoredBytes, byKind, byStatus, failed24h }." + _Admin).Returns("MediaSummary"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", DetailAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a media item", _Tag, "Item fields plus processor, processorJobId, processingStartedUtc, attempts, and variants." + _Admin)
                    .PathParam("id", "Media identifier").Returns("AdminMediaDetail").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}", UpdateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit alt text, labels, and tags", _Tag, "Body { altText?, labels?, tags? }." + _Admin)
                    .PathParam("id", "Media identifier").Body("{ altText?, labels?, tags? }").Returns("AdminMediaDetail").Resp(400, "ValidationFailed").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/reprocess", ReprocessAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reprocess a failed item", _Tag, "Failed items whose original still exists go back to Processing." + _Admin)
                    .PathParam("id", "Media identifier").Returns("AdminMediaDetail").Resp(404, "NotFound").Resp(409, "NotReprocessable"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}", DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a media item", _Tag, "Detaches it from its target (the content keeps its other attachments) and deletes every object." + _Admin)
                    .PathParam("id", "Media identifier").Resp(204, "Deleted").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private async Task EnumerateAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Media.EnumerateAdminAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Media.EnumerateAdminAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SummaryAsync(HttpContextBase ctx)
        {
            MediaSummary summary = await _Directory.For(ctx).Media.SummaryAsync(ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, summary).ConfigureAwait(false);
        }

        private async Task DetailAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Media.AdminDetailAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            MediaUpdateRequest request = RouteHelper.ReadJson<MediaUpdateRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Media.AdminUpdateAsync(actor, RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ReprocessAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Media.ReprocessAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            await _Directory.For(ctx).Media.AdminDeleteAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        #endregion
    }
}
