namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Metrics;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin feed management (Phase 2 contract section 10.1): the admin feed enumeration, detail, create (as a chosen
    /// author), edit, delete, pin and unpin, publish now, and engagement metrics. Admin group (SystemAdmin or the
    /// break-glass token; creating needs a member session).
    /// </summary>
    public class AdminFeedRoutes
    {
        #region Private-Members

        private const string _Tag = "Admin feed";
        private const string _Base = Constants.ApiPrefix + "/admin/feed";
        private const string _Item = "{ id, kind, title, excerpt, author, state, visibility, audience, isPinned, publishAtUtc, publishedUtc, createdUtc, editedUtc, deletedUtc, attachmentCount, hasVideo, commentCount, reactionTotal, openReportCount, labels, tags }";
        private const string _Filters = "state, authorUserId, pinned, audience, publishedAfterUtc, publishedBeforeUtc, fromUtc, toUtc";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminFeedRoutes(PropertyDirectory directory)
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
            string list = "Every post including Deleted. The standard createdAfterUtc and createdBeforeUtc window applies to createdUtc. Filters state (any state or Deleted), authorUserId, pinned, audience, and the published window publishedAfterUtc and publishedBeforeUtc (aliases fromUtc and toUtc; on publishedUtc, or createdUtc when unpublished; a name and its alias with different values is 400). Search covers title and body. Exact totals. Admin.";
            post.Static.Add(HttpMethod.GET, _Base, ListAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List the admin feed", _Tag, list).Enumeration(_Item, ContentCollections.AdminFeedOrderings, _Filters).Resp(400, "ValidationFailed or InvalidContinuationToken"));
            post.Static.Add(HttpMethod.POST, _Base + "/enumerate", EnumerateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate the admin feed", _Tag, "EnumerationQuery body; same result as the GET form. " + list)
                    .Body("EnumerationQuery", false).Enumeration(_Item, ContentCollections.AdminFeedOrderings, _Filters, false).Resp(400, "ValidationFailed or InvalidContinuationToken"));
            post.Static.Add(HttpMethod.POST, _Base, CreateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a feed post (admin)", _Tag, "Body of POST /api/v1/feed plus authorUserId (must have canPostToFeed or SystemAdmin; default the caller). Needs a member session: the admin token gets 403.")
                    .Body("FeedPostRequest").Returns("FeedPost", 201).Resp(400, "ValidationFailed, MediaLimitExceeded, or ContentFiltered").Resp(403, "Forbidden")
                    .Resp(409, "MediaNotAttachable or PinLimitReached").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a feed post (admin)", _Tag, "FeedPost in any state (including Deleted with deletedUtc) plus revisions and moderationActions previews ({ objects (at most 5), totalRecords, enumerate }) and reportSummary { open, total }. Admin.")
                    .PathParam("id", "Post identifier").Returns("AdminFeedPostDetail").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}", UpdateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit a feed post (admin)", _Tag, "Body of PUT /api/v1/feed/{id}; no edit window. Admin.")
                    .PathParam("id", "Post identifier").Body("FeedPostRequest").Returns("FeedPost").Resp(400, "ValidationFailed, MediaLimitExceeded, or ContentFiltered").Resp(404, "NotFound").Resp(409, "MediaNotAttachable"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}", DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a feed post (admin)", _Tag, "Soft delete. Admin.").PathParam("id", "Post identifier").Resp(204, "Deleted").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/pin", PinAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Pin a feed post (admin)", _Tag, "Published posts only; idempotent. Admin.")
                    .PathParam("id", "Post identifier").Returns("FeedPost").Resp(404, "NotFound").Resp(409, "PinLimitReached or NotPublished"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/unpin", UnpinAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Unpin a feed post (admin)", _Tag, "Idempotent. Admin.").PathParam("id", "Post identifier").Returns("FeedPost").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/publish-now", PublishNowAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Publish a scheduled post now", _Tag, "Scheduled posts only; the post still waits for its media (Processing until every attachment is Ready). Admin.")
                    .PathParam("id", "Post identifier").Returns("FeedPost").Resp(404, "NotFound").Resp(409, "NotScheduled"));
            post.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/admin/metrics/engagement", EngagementAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Engagement metrics", _Tag, "{ days, postsByDay, commentsByDay, reactionsByDay ([{ date, count }], one per day), activeMembers, openReports, scheduledPosts }. Query days 7 to 365 (default 30). Admin.")
                    .QueryParam("days", "Window in days (7 to 365).").Enumeration("LegacyMetricRow", "CreatedDescending, CreatedAscending", "days, series", true).Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/admin/metrics/engagement/enumerate", EngagementAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Engagement metrics", _Tag, "{ days, postsByDay, commentsByDay, reactionsByDay ([{ date, count }], one per day), activeMembers, openReports, scheduledPosts }. Query days 7 to 365 (default 30). Admin.")
                    .QueryParam("days", "Window in days (7 to 365).").Body("EnumerationQuery", false).Enumeration("LegacyMetricRow", "CreatedDescending, CreatedAscending", "days, series", true).Resp(400, "ValidationFailed"));
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task<ContentActor> AdminAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Admin access is required.");
            return actor;
        }

        private Task ListAsync(HttpContextBase ctx)
        {
            return SendListAsync(ctx, RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx));
        }

        private Task EnumerateAsync(HttpContextBase ctx)
        {
            return SendListAsync(ctx, RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx));
        }

        private async Task SendListAsync(HttpContextBase ctx, EnumerationQuery query)
        {
            await AdminAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Feed.ListAdminAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            FeedPostRequest request = RouteHelper.ReadJson<FeedPostRequest>(ctx);
            ContentActor actor = await AdminAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Feed.AdminCreateAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await AdminAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Feed.GetAdminAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            FeedPostRequest request = RouteHelper.ReadJson<FeedPostRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await AdminAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Feed.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            ContentActor actor = await AdminAsync(ctx).ConfigureAwait(false);
            await _Directory.For(ctx).Feed.DeleteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task PinAsync(HttpContextBase ctx)
        {
            ContentActor actor = await AdminAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Feed.SetPinnedAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), true, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UnpinAsync(HttpContextBase ctx)
        {
            ContentActor actor = await AdminAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Feed.SetPinnedAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), false, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PublishNowAsync(HttpContextBase ctx)
        {
            ContentActor actor = await AdminAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Feed.PublishNowAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task EngagementAsync(HttpContextBase ctx)
        {
            EnumerationQuery query=ctx.Request.Method==HttpMethod.GET?RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx):RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            int days=AggregateEnumeration.Days(query);
            await AdminAsync(ctx).ConfigureAwait(false);
            ApplicationServices services=_Directory.For(ctx);
            TrellisKit.Core.Responses.EngagementMetrics summary=await services.Feed.EngagementAsync(days, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx,200,AggregateEnumeration.Daily(services,"engagement-daily",query,summary)).ConfigureAwait(false);
        }

        #endregion
    }
}
