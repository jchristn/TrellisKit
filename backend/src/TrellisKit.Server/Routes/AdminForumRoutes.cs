namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Services.Metrics;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin and moderator forum routes (Phase 3 contract sections 11, 12, and 15): forum management, moderators per
    /// forum, the thread and trending queues, forum metrics, forum mutes, and the search rebuild.
    /// </summary>
    public class AdminForumRoutes
    {
        #region Private-Members

        private const string _Tag = "Admin forums";
        private const string _ModerationTag = "Moderation";
        private const string _AdminForum = "AdminForum (Forum plus openReportCount, moderatorCount, pinnedCount, threadsLast30d)";
        private const string _ForumModerator = "{ user: UserSummary, roles, scope: Forum | AllForums, forumId, grantedUtc, grantedByName, labels, tags }";
        private const string _Trending = "{ thread: ThreadSummary, score, breakdown: { comments, commenters, reactions, ageHours }, promotion, computedUtc }";
        private const string _Mute = "{ id, user: UserSummary, forum: { id, name, slug }, untilUtc, reason, mutedBy: UserSummary, createdUtc, endedUtc, labels, tags }";
        private const string _Base = Constants.ApiPrefix + "/admin/forums";
        private const string _Mutes = Constants.ApiPrefix + "/admin/moderation/forum-mutes";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminForumRoutes(PropertyDirectory directory)
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
            EnumerationRoutes.Add(post, _Base, ForumCollections.AdminForums, _AdminForum,
                () => Doc("List forums (admin)", _Tag, "Every forum including archived ones, with admin counts (openReportCount, moderatorCount, pinnedCount, threadsLast30d). Filters section, includeArchived, isArchived, followedOnly, audience. Exact totals. Admin."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Forums.Catalog.ListAdminAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.POST, _Base, CreateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a forum", _Tag, "Body { name, slug?, description, guidelines, icon { emoji, mediaId } (an emoji of one grapheme or a Ready image owned by an admin; the response adds imageUrl), color, section, audience, threadCreation: Members | Moderators | Admins, allowedThreadTypes, defaultOrdering, allowMedia, allowVideo, maxOpenFeedbackPerMember, labels, tags }. At most forums.maxForums. Audited as forum.create. Admin.")
                    .Body("ForumRequest").Returns("Forum", 201).Resp(400, "ValidationFailed").Resp(409, "SlugInUse or ForumLimitReached"));
            post.Static.Add(HttpMethod.PUT, _Base + "/order", OrderAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reorder forums", _Tag, "Body { forumIds }: every non-archived forum exactly once. Audited as forum.order. Admin.")
                    .Body("ForumOrderRequest").Returns("{ updated }").Resp(400, "ValidationFailed"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a forum (admin)", _Tag, "The admin forum with a moderators preview of at most 10 (totalRecords and the enumerate path). Admin.")
                    .PathParam("id", "Forum identifier").Bounded("AdminForumDetail", "moderators preview of at most 10 with totalRecords and the enumerate path").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}", UpdateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit a forum", _Tag, "Any create field; omitted fields stay. Narrowing the audience narrows every thread in the forum. Audited as forum.update. Admin.")
                    .PathParam("id", "Forum identifier").Body("ForumRequest").Returns("Forum").Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(409, "SlugInUse"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/archive", ctx => ArchiveAsync(ctx, true), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Archive a forum", _Tag, "Body { reason? }. Archived forums are read-only and hidden from the default list. Idempotent. Audited as forum.archive. Admin.")
                    .PathParam("id", "Forum identifier").Body("ReasonRequest", false).Returns("Forum").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/unarchive", ctx => ArchiveAsync(ctx, false), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Unarchive a forum", _Tag, "Body { reason? }. Idempotent. Audited as forum.unarchive. Admin.")
                    .PathParam("id", "Forum identifier").Body("ReasonRequest", false).Returns("Forum").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}", DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a forum", _Tag, "Only a forum with no threads; archive one that has threads. Audited as forum.delete. Admin.")
                    .PathParam("id", "Forum identifier").Resp(204, "Deleted").Resp(404, "NotFound").Resp(409, "ForumNotEmpty"));
            EnumerationRoutes.Add(post, _Base + "/{id}/moderators", ForumCollections.ForumModerators, _ForumModerator,
                () => Doc("List a forum's moderators", _Tag, "Members whose Moderator assignments cover the forum (the forum, every forum, or Global). Grant and revoke with /api/v1/admin/moderators (resourceType Forum). Admin."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Forums.Catalog.ModeratorsAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token)), "id");
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/admin/threads", ForumCollections.AdminThreads, "ThreadSummary (plus state)",
                () => Doc("List threads (moderation)", _ModerationTag, "Threads in every state (filters.state: Processing, Published, Hidden, Removed, Deleted) in the caller's moderation scope. Filters forumIds, type, feedbackStatus, authorUserId, pinned, locked, promoted, audience. Moderator."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Forums.Threads.ListAdminAsync(actor, query, ctx.Token)));
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/admin/trending", ForumCollections.Trending, _Trending,
                () => Doc("List trending threads", _ModerationTag, "Threads by trending score (ScoreDescending, the default) with the score breakdown and promotion state, for promotion decisions. SystemAdmin sees every forum; moderators their scope. Filters forumIds, type, promoted, minScore. Moderator."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Forums.Trending.QueueAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/admin/metrics/forums", MetricsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Forum metrics", _Tag, "Query days (7 to 365, default 30). Totals plus one entry per day for threads, comments, votes, and open feedback requests, and the top forums by threads (at most 10). Admin.")
                    .QueryParam("days", "7 to 365, default 30").Enumeration("LegacyMetricRow", "CreatedDescending, CreatedAscending", "days, series", true).Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/admin/metrics/forums/enumerate", MetricsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Forum metrics", _Tag, "Query days (7 to 365, default 30). Totals plus one entry per day for threads, comments, votes, and open feedback requests, and the top forums by threads (at most 10). Admin.")
                    .QueryParam("days", "7 to 365, default 30").Body("EnumerationQuery", false).Enumeration("LegacyMetricRow", "CreatedDescending, CreatedAscending", "days, series", true).Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, _Mutes, CreateMuteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Mute a member in a forum", _ModerationTag, "Body { userId, forumId, untilUtc? (null: until lifted; at most forums.maxForumMuteDays when set), reason (3 to 500), notifyMember? (default true) }. Replaces an active mute in that forum. The member can read but not post, comment, react, or vote there. Audited as ForumMute. Moderator in scope of the forum.")
                    .Body("ForumMuteRequest").Returns(_Mute, 201).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound"));
            EnumerationRoutes.Add(post, _Mutes, ForumCollections.ForumMutes, _Mute,
                () => Doc("List forum mutes", _ModerationTag, "Mutes in the caller's scope, newest first. Filters forumId, userId, activeOnly (default true). Search covers the reason. Moderator."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Forums.Moderation.ListMutesAsync(actor, query, ctx.Token)));
            post.Parameter.Add(HttpMethod.DELETE, _Mutes + "/{id}", EndMuteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Lift a forum mute", _ModerationTag, "Body { reason? }. Idempotent. Audited as ForumUnmute. Moderator in scope of the forum.")
                    .PathParam("id", "Mute identifier").Body("ReasonRequest", false).Resp(204, "Lifted").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/admin/search/rebuild", RebuildAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Rebuild the search index", _Tag, "Clears the index and queues every feed post, thread, comment, and member; the indexer catches up in the background. Audited as search.rebuild. Admin.")
                    .Returns("{ queued, requestedUtc }", 202).Resp(404, "NotAvailable (search.enabled is off)"));
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task<object> RunAsync<T>(HttpContextBase ctx, Func<ContentActor, Task<T>> body) where T : class
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            return await body(actor).ConfigureAwait(false);
        }

        private async Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            return await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            ForumRequest request = RouteHelper.ReadJson<ForumRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Forums.Catalog.CreateAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task OrderAsync(HttpContextBase ctx)
        {
            ForumOrderRequest request = RouteHelper.ReadJson<ForumOrderRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Catalog.OrderAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Catalog.GetAdminAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            ForumRequest request = RouteHelper.ReadJson<ForumRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Catalog.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ArchiveAsync(HttpContextBase ctx, bool archived)
        {
            ReasonRequest request = RouteHelper.ReadJson<ReasonRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Catalog.SetArchivedAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), archived, request.Reason, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await _Directory.For(ctx).Forums.Catalog.DeleteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task MetricsAsync(HttpContextBase ctx)
        {
            EnumerationQuery query=ctx.Request.Method==HttpMethod.GET?RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx):RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            int days=AggregateEnumeration.Days(query);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            ApplicationServices services=_Directory.For(ctx);
            TrellisKit.Core.Responses.ForumMetrics summary=await services.Forums.Catalog.MetricsAsync(actor, days, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx,200,AggregateEnumeration.Daily(services,"forums-daily",query,summary)).ConfigureAwait(false);
        }

        private async Task CreateMuteAsync(HttpContextBase ctx)
        {
            ForumMuteRequest request = RouteHelper.ReadJson<ForumMuteRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Forums.Moderation.CreateMuteAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task EndMuteAsync(HttpContextBase ctx)
        {
            ReasonRequest request = RouteHelper.ReadJson<ReasonRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await _Directory.For(ctx).Forums.Moderation.EndMuteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request.Reason, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task RebuildAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            int queued = await _Directory.For(ctx).Forums.Search.RebuildAsync(actor, Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 202, new SearchRebuildResponse { Queued = queued, RequestedUtc = DateTime.UtcNow }).ConfigureAwait(false);
        }

        #endregion
    }
}
