namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Saves and private collections (REST_API.md, Saves): save and unsave any supported target, list saves with
    /// target summaries, batch saved state, and collection create, rename, order, and delete. Signed-in members only;
    /// 404 NotAvailable when features.saves is false; 503 FeatureDisabled under the saves kill switch.
    /// </summary>
    public class FavoriteRoutes
    {
        #region Private-Members

        private const string _Tag = "Saves";
        private const string _Saves = Constants.ApiPrefix + "/me/saves";
        private const string _Collections = Constants.ApiPrefix + "/me/save-collections";
        private const string _View = "FavoriteView { id, targetType, targetId, note, collectionIds, target { available, title, imageUrl, href }, labels, tags, createdUtc, lastUpdateUtc }";
        private const string _Collection = "FavoriteCollection { id, userId, name, position, itemCount, labels, tags, createdUtc, lastUpdateUtc }";
        private const string _Gate = " Signed in. 404 NotAvailable when features.saves is false; 503 FeatureDisabled under the saves kill switch.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public FavoriteRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the routes.
        /// </summary>
        /// <param name="server">Webserver.</param>
        /// <exception cref="ArgumentNullException">The server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;

            EnumerationRoutes.Add(post, _Saves, SaveCollections.Saves, _View,
                () => Doc("List your saves", _Tag, "Your saves with target summaries. A target you can no longer open (archived, unpublished, moderated, feature off, or access lost) has target.available false and no title, image, or link. Orderings CreatedDescending (default), CreatedAscending, UpdatedDescending. Filters targetTypes (array or comma list), collectionId (404 NotFound when not yours). Search covers notes. Member maximum 100." + _Gate),
                async (ctx, query) =>
                {
                    ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                    return await _Directory.For(ctx).Favorites.ListAsync(actor, query, ctx.Token).ConfigureAwait(false);
                });
            post.Static.Add(HttpMethod.POST, _Saves + "/state", async ctx =>
            {
                FavoriteStateRequest request = RouteHelper.ReadJson<FavoriteStateRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Favorites.StateAsync(actor, request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Read saved state", _Tag, "Body { targets: [{ targetType, targetId }] } with 1 to 100 targets. Returns { items: [{ targetType, targetId, saved, favoriteId }] } in request order (fixed bound: the request's targets)." + _Gate)
                .Body("FavoriteStateRequest").Returns("FavoriteStateResponse { items }").Resp(400, "ValidationFailed").Resp(404, "NotAvailable"));
            post.Parameter.Add(HttpMethod.PUT, _Saves + "/{targetType}/{targetId}", async ctx =>
            {
                FavoriteRequest request = RouteHelper.ReadJson<FavoriteRequest>(ctx, out HashSet<string> present);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Favorites.SaveAsync(actor, RouteHelper.Param(ctx, "targetType"), RouteHelper.Param(ctx, "targetId"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Save a target", _Tag, "Idempotent: creates the save or updates it. targetType is Lesson, Series, CatalogEntry, Venue, Instructor, Event, Post, or Thread. Body (all optional) { collectionIds (at most 20 of your collections; replaces the set), note (at most 500 characters, visible to you and moderators; null clears), labels, tags }. Notes pass the word filter and content review. 402 MembershipRequired when saves are gated and you have no membership." + _Gate)
                .PathParam("targetType", "Target type").PathParam("targetId", "Target identifier").Body("FavoriteRequest").Returns(_View)
                .Resp(400, "ValidationFailed or ContentFiltered").Resp(402, "MembershipRequired").Resp(403, "Forbidden (you cannot open the target)").Resp(404, "NotFound (target or collection) or NotAvailable (saves or the target type off)").Resp(409, "LimitReached (saves.maxPerMember)"));
            post.Parameter.Add(HttpMethod.DELETE, _Saves + "/{targetType}/{targetId}", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await _Directory.For(ctx).Favorites.UnsaveAsync(actor, RouteHelper.Param(ctx, "targetType"), RouteHelper.Param(ctx, "targetId"), ctx.Token).ConfigureAwait(false);
                await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Remove a save", _Tag, "Idempotent; 204 whether or not the target was saved." + _Gate)
                .PathParam("targetType", "Target type").PathParam("targetId", "Target identifier").Resp(204, "Removed").Resp(400, "ValidationFailed").Resp(404, "NotAvailable"));

            EnumerationRoutes.Add(post, _Collections, SaveCollections.Collections, _Collection,
                () => Doc("List your save collections", _Tag, "Your collections with item counts. Orderings PositionAscending (default), NameAscending, CreatedDescending, CreatedAscending. Search covers names." + _Gate),
                async (ctx, query) =>
                {
                    ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                    return await _Directory.For(ctx).Favorites.ListCollectionsAsync(actor, query, ctx.Token).ConfigureAwait(false);
                });
            post.Static.Add(HttpMethod.POST, _Collections, async ctx =>
            {
                FavoriteCollectionRequest request = RouteHelper.ReadJson<FavoriteCollectionRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Favorites.CreateCollectionAsync(actor, request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Create a save collection", _Tag, "Body { name (1 to 60 characters, unique per member, case-insensitive), labels, tags }. Added at the end of your order." + _Gate)
                .Body("FavoriteCollectionRequest").Returns(_Collection, 201).Resp(400, "ValidationFailed or ContentFiltered").Resp(402, "MembershipRequired").Resp(404, "NotAvailable").Resp(409, "DuplicateName or LimitReached (saves.maxCollectionsPerMember)"));
            post.Static.Add(HttpMethod.PUT, _Collections + "/order", async ctx =>
            {
                FavoriteCollectionOrderRequest request = RouteHelper.ReadJson<FavoriteCollectionOrderRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await _Directory.For(ctx).Favorites.ReorderCollectionsAsync(actor, request, ctx.Token).ConfigureAwait(false);
                await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Order save collections", _Tag, "Body { ids }: every one of your collections exactly once (at most saves.maxCollectionsPerMember)." + _Gate)
                .Body("FavoriteCollectionOrderRequest").Resp(204, "Ordered").Resp(400, "ValidationFailed").Resp(404, "NotAvailable"));
            post.Parameter.Add(HttpMethod.PUT, _Collections + "/{id}", async ctx =>
            {
                FavoriteCollectionRequest request = RouteHelper.ReadJson<FavoriteCollectionRequest>(ctx);
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Favorites.UpdateCollectionAsync(actor, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Update a save collection", _Tag, "Body { name, labels, tags } (each optional; omitted keeps the value)." + _Gate)
                .PathParam("id", "Collection identifier").Body("FavoriteCollectionRequest").Returns(_Collection).Resp(400, "ValidationFailed or ContentFiltered").Resp(404, "NotFound or NotAvailable").Resp(409, "DuplicateName"));
            post.Parameter.Add(HttpMethod.DELETE, _Collections + "/{id}", async ctx =>
            {
                ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
                await _Directory.For(ctx).Favorites.DeleteCollectionAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
                await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: Doc("Delete a save collection", _Tag, "Removes the grouping only; its saves stay." + _Gate)
                .PathParam("id", "Collection identifier").Resp(204, "Deleted").Resp(404, "NotFound or NotAvailable"));
        }

        #endregion

        #region Private-Methods

        private Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            return _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token);
        }

        #endregion
    }
}
