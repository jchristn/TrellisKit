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
    /// Moderators admin (Phase 2 contract section 10.4, Admin group): the moderators enumeration, grant, and revoke of
    /// FeedModerator.
    /// </summary>
    public class AdminModeratorRoutes
    {
        #region Private-Members

        private const string _Tag = "Moderators";
        private const string _Base = Constants.ApiPrefix + "/admin/moderators";
        private const string _Item = "{ user: UserSummary, roles: [{ role, resourceType, resourceId }], grantedUtc, grantedByName, openInScope, actions30d, medianResponseMinutes30d, overdueHandled30d, labels, tags }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminModeratorRoutes(PropertyDirectory directory)
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
            EnumerationRoutes.Add(post, _Base, ModerationCollections.Moderators, _Item,
                () => Doc("List moderators", _Tag, "Members holding Moderator or FeedModerator (not deleted) with 30-day statistics. Orderings DisplayNameAscending (default) and the Created orderings. Search covers display name and handle. Filters role (Moderator, FeedModerator) and resourceType. Admin."),
                async (ctx, query) =>
                {
                    ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
                    return await _Directory.For(ctx).Moderators.EnumerateAsync(actor, query, ctx.Token).ConfigureAwait(false);
                });
            post.Static.Add(HttpMethod.POST, _Base, GrantAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Grant FeedModerator or a forum Moderator", _Tag, "Body { userId, resourceType: Feed | Forum, resourceId }. Feed grants FeedModerator (resourceId *). Forum grants Moderator for one forum (resourceId a forum id) or every forum (resourceId *, the default) and returns the row with scope (Forum or AllForums) and forumId. Idempotent; effective on the member's next request. Audited as member.roles. Admin.")
                    .Body("ModeratorGrantRequest").Returns("ModeratorListItem").Resp(400, "ValidationFailed").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{userId}", RevokeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Revoke FeedModerator or a forum Moderator", _Tag, "Revokes FeedModerator for resourceType Feed (the default), or the forum Moderator grant for resourceType Forum and resourceId (a forum id, or * for the all-forums grant); idempotent; past actions stay attributed. Audited as member.roles. Admin.")
                    .PathParam("userId", "Member identifier").QueryParam("resourceType", "Feed or Forum").QueryParam("resourceId", "Forum identifier or * (Forum only)").Resp(204, "Revoked").Resp(400, "ValidationFailed").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task GrantAsync(HttpContextBase ctx)
        {
            ModeratorGrantRequest request = RouteHelper.ReadJson<ModeratorGrantRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Moderators.GrantAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RevokeAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await _Directory.For(ctx).Moderators.RevokeAsync(actor, Info(ctx), RouteHelper.Param(ctx, "userId"), RouteHelper.Query(ctx, "resourceType"), RouteHelper.Query(ctx, "resourceId"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        #endregion
    }
}
