namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Reaction routes (Phase 2 contract section 6): the property's set, react and unreact on posts and comments, and
    /// who reacted. Members only.
    /// </summary>
    public class ReactionRoutes
    {
        #region Private-Members

        private const string _Tag = "Reactions";
        private const string _Item = "{ id, user: UserSummary, key, createdUtc, labels, tags }";
        private const string _Orderings = "CreatedDescending, CreatedAscending";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public ReactionRoutes(PropertyDirectory directory)
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
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/reactions/set", AdministrativeCatalogs.Reactions, "ReactionSetItem",
                () => Doc("Enumerate reaction set", _Tag, "Property-configured reactions, with likeKey context. Declaration timestamps use Unix epoch."),
                async (ctx, query) =>
                {
                    ApplicationServices services = _Directory.For(ctx);
                    await services.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
                    EnumerationRequest request = services.Enumeration.Prepare(query, AdministrativeCatalogs.Reactions, false);
                    ReactionSetResponse set = services.Reactions.GetSet();
                    return CatalogEnumerationResponse<ReactionSetItem>.From(CatalogEnumeration.Page(request, set.Items, item => item.Key, search: item => item.Key + " " + item.Label, orderKey: item => set.Items.IndexOf(item).ToString(CultureInfo.InvariantCulture)), new Dictionary<string, object?> { ["likeKey"] = set.LikeKey });
                });

            foreach (string segment in new[] { "feed", "comments", "threads" })
            {
                TargetTypeEnum type = segment == "feed" ? TargetTypeEnum.FeedPost : (segment == "threads" ? TargetTypeEnum.Thread : TargetTypeEnum.Comment);
                string noun = segment == "feed" ? "a feed post" : (segment == "threads" ? "a thread (Original threads; a LinkedFeedPost thread answers 409 UseCommentTarget; a locked thread 409 ThreadLocked)" : "a comment");
                string path = Constants.ApiPrefix + "/" + segment + "/{id}/reactions";
                post.Parameter.Add(HttpMethod.PUT, path + "/{key}", ctx => ChangeAsync(ctx, type, true), RouteHelper.HandleExceptionAsync,
                    openApiMetadata: Doc("React to " + noun, _Tag, "Adds the caller's reaction key. Idempotent; several keys per member. Reacting to your own content does not notify. Members who are not muted.")
                        .PathParam("id", "Target identifier").PathParam("key", "Reaction key").Returns("{ reactionCounts, reactionTotal, myReactions }")
                        .Resp(400, "UnknownReaction").Resp(403, "Muted or InteractionBlocked").Resp(404, "NotFound").Resp(429, "RateLimited (reactions.perMinute)"));
                post.Parameter.Add(HttpMethod.DELETE, path + "/{key}", ctx => ChangeAsync(ctx, type, false), RouteHelper.HandleExceptionAsync,
                    openApiMetadata: Doc("Remove a reaction from " + noun, _Tag, "Removes the caller's reaction key. Idempotent. Members who are not muted.")
                        .PathParam("id", "Target identifier").PathParam("key", "Reaction key").Returns("{ reactionCounts, reactionTotal, myReactions }")
                        .Resp(400, "UnknownReaction").Resp(403, "Muted or InteractionBlocked").Resp(404, "NotFound").Resp(429, "RateLimited (reactions.perMinute)"));
                post.Parameter.Add(HttpMethod.GET, path, ctx => ListAsync(ctx, type, false), RouteHelper.HandleExceptionAsync,
                    openApiMetadata: Doc("Who reacted to " + noun, _Tag, "Newest first; filter key; members you blocked are omitted. maxResults default 50, max 100. Members only.")
                        .PathParam("id", "Target identifier").Enumeration(_Item, _Orderings, "key").Resp(400, "ValidationFailed, UnknownReaction, or InvalidContinuationToken").Resp(404, "NotFound").Resp(429, "RateLimited"));
                post.Parameter.Add(HttpMethod.POST, path + "/enumerate", ctx => ListAsync(ctx, type, true), RouteHelper.HandleExceptionAsync,
                    openApiMetadata: Doc("Enumerate who reacted to " + noun, _Tag, "EnumerationQuery body (filters.key); same result as the GET form. Members only.")
                        .PathParam("id", "Target identifier").Body("EnumerationQuery", false).Enumeration(_Item, _Orderings, "key", false)
                        .Resp(400, "ValidationFailed, UnknownReaction, or InvalidContinuationToken").Resp(404, "NotFound").Resp(429, "RateLimited"));
            }
        }

        #endregion

        #region Private-Methods

        private async Task ChangeAsync(HttpContextBase ctx, TargetTypeEnum type, bool add)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            string id = RouteHelper.Param(ctx, "id");
            string key = RouteHelper.Param(ctx, "key");
            object state = add
                ? await _Directory.For(ctx).Reactions.AddAsync(actor, type, id, key, ctx.Token).ConfigureAwait(false)
                : await _Directory.For(ctx).Reactions.RemoveAsync(actor, type, id, key, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, state).ConfigureAwait(false);
        }

        private async Task ListAsync(HttpContextBase ctx, TargetTypeEnum type, bool body)
        {
            EnumerationQuery query = body ? RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx) : RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Reactions.ListAsync(actor, type, RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
