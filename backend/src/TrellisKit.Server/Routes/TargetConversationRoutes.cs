namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.Routing;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Conversation routes of a content target provider type (docs/PHASE4_CONTRACT.md section 6): the comment tree,
    /// commenting, and reactions on the object itself, under a feature's own prefix (for example /api/v1/events with
    /// Event, /api/v1/lessons with Lesson). Comment edits, deletes, children, permalinks, and comment reactions use the
    /// target-independent /api/v1/comments routes. The provider registered for the type decides visibility and the
    /// write rules.
    /// </summary>
    public static class TargetConversationRoutes
    {
        #region Private-Members

        private const string _ReactionItem = "{ id, user: UserSummary, key, createdUtc, labels, tags }";
        private const string _CommentItem = "Comment (with children and moreChildren)";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register GET and POST {prefix}/{id}/comments/enumerate (the comment tree), POST {prefix}/{id}/comments
        /// (comment or reply), PUT and DELETE {prefix}/{id}/reactions/{key}, and GET and POST
        /// {prefix}/{id}/reactions[/enumerate] (who reacted), with OpenAPI documentation.
        /// </summary>
        /// <param name="post">Post-authentication routing group.</param>
        /// <param name="directory">Property directory.</param>
        /// <param name="prefix">Collection path, for example /api/v1/events (no trailing slash).</param>
        /// <param name="type">Target type served by a content target provider.</param>
        /// <param name="tag">OpenAPI tag.</param>
        /// <param name="featureLabel">Noun used in summaries, for example "an event".</param>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        public static void Register(RoutingGroup post, PropertyDirectory directory, string prefix, TargetTypeEnum type, string tag, string featureLabel)
        {
            ArgumentNullException.ThrowIfNull(post);
            ArgumentNullException.ThrowIfNull(directory);
            if (String.IsNullOrWhiteSpace(prefix)) throw new ArgumentNullException(nameof(prefix));
            if (String.IsNullOrWhiteSpace(tag)) throw new ArgumentNullException(nameof(tag));
            string noun = String.IsNullOrWhiteSpace(featureLabel) ? "an object" : featureLabel;
            string root = prefix.TrimEnd('/') + "/{id}";
            string comments = root + "/comments";
            string tree = "Top-level comments on " + noun + " with replies nested up to depth levels (filter depth 1 to 5, default 3) and childLimit replies each (filter childLimit 1 to 20, default 5); " +
                "the result adds commentCount. Orderings Top (default), CreatedDescending, CreatedAscending. moreChildren continues through POST /api/v1/comments/{id}/children/enumerate. " +
                "Moderators of the scope see hidden and removed comments. maxResults default 20, max 50. 404 when the caller cannot read the " + type + ".";

            post.Parameter.Add(HttpMethod.GET, comments, ctx => TreeAsync(ctx, directory, type, RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx, "id")), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List the comment tree of " + noun, tag, tree)
                    .PathParam("id", type + " identifier").Enumeration(_CommentItem, ContentCollections.CommentOrderings, "depth, childLimit")
                    .Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(402, "MembershipRequired").Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.POST, comments + "/enumerate", ctx => TreeAsync(ctx, directory, type, RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx)), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate the comment tree of " + noun, tag, "EnumerationQuery body (filters.depth, filters.childLimit); same result as the GET form. " + tree)
                    .PathParam("id", type + " identifier").Body("EnumerationQuery", false).Enumeration(_CommentItem, ContentCollections.CommentOrderings, "depth, childLimit", false)
                    .Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(402, "MembershipRequired").Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.POST, comments, ctx => CreateAsync(ctx, directory, type), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Comment on " + noun, tag, "Body { body, parentCommentId?, mediaIds?, showLinkPreview?, labels?, tags? } as for feed comments. The feature's rules apply (commentsEnabled, media settings, canceled events, gating). Mentions notify with the object's path. Members with canPost who are not muted.")
                    .PathParam("id", type + " identifier").Body("CommentRequest").Returns("Comment", 201).Resp(400, "ValidationFailed, MediaLimitExceeded, or ContentFiltered")
                    .Resp(402, "MembershipRequired").Resp(403, "PostingNotAllowed, CommentsDisabled, MediaNotAllowed, VideoUploadNotAllowed, Muted, or InteractionBlocked").Resp(404, "NotFound")
                    .Resp(409, "EventCanceled, ParentUnavailable, or MediaNotAttachable").Resp(429, "RateLimited (comments per minute and day)"));

            string reactions = root + "/reactions";
            post.Parameter.Add(HttpMethod.PUT, reactions + "/{key}", ctx => ReactAsync(ctx, directory, type, true), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("React to " + noun, tag, "Adds the caller's reaction key. Idempotent; several keys per member. Members who are not muted and may read the " + type + ".")
                    .PathParam("id", type + " identifier").PathParam("key", "Reaction key").Returns("{ reactionCounts, reactionTotal, myReactions }")
                    .Resp(400, "UnknownReaction").Resp(402, "MembershipRequired").Resp(403, "Muted or InteractionBlocked").Resp(404, "NotFound").Resp(429, "RateLimited (reactions.perMinute)"));
            post.Parameter.Add(HttpMethod.DELETE, reactions + "/{key}", ctx => ReactAsync(ctx, directory, type, false), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Remove a reaction from " + noun, tag, "Removes the caller's reaction key. Idempotent. Members who are not muted.")
                    .PathParam("id", type + " identifier").PathParam("key", "Reaction key").Returns("{ reactionCounts, reactionTotal, myReactions }")
                    .Resp(400, "UnknownReaction").Resp(403, "Muted or InteractionBlocked").Resp(404, "NotFound").Resp(429, "RateLimited (reactions.perMinute)"));
            post.Parameter.Add(HttpMethod.GET, reactions, ctx => ReactionsAsync(ctx, directory, type, RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx, "id")), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Who reacted to " + noun, tag, "Newest first; filter key; members you blocked are omitted. maxResults default 50, max 100.")
                    .PathParam("id", type + " identifier").Enumeration(_ReactionItem, "CreatedDescending, CreatedAscending", "key")
                    .Resp(400, "ValidationFailed, UnknownReaction, or InvalidContinuationToken").Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.POST, reactions + "/enumerate", ctx => ReactionsAsync(ctx, directory, type, RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx)), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate who reacted to " + noun, tag, "EnumerationQuery body (filters.key); same result as the GET form.")
                    .PathParam("id", type + " identifier").Body("EnumerationQuery", false).Enumeration(_ReactionItem, "CreatedDescending, CreatedAscending", "key", false)
                    .Resp(400, "ValidationFailed, UnknownReaction, or InvalidContinuationToken").Resp(404, "NotFound").Resp(429, "RateLimited"));
        }

        #endregion

        #region Private-Methods

        private static async Task TreeAsync(HttpContextBase ctx, PropertyDirectory directory, TargetTypeEnum type, EnumerationQuery query)
        {
            ApplicationServices services = directory.For(ctx);
            ContentActor actor = await services.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await services.Comments.TargetTreeAsync(actor, type, RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private static async Task CreateAsync(HttpContextBase ctx, PropertyDirectory directory, TargetTypeEnum type)
        {
            CommentRequest request = RouteHelper.ReadJson<CommentRequest>(ctx);
            ApplicationServices services = directory.For(ctx);
            ContentActor actor = await services.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await services.Comments.CreateOnTargetAsync(actor, type, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private static async Task ReactAsync(HttpContextBase ctx, PropertyDirectory directory, TargetTypeEnum type, bool add)
        {
            ApplicationServices services = directory.For(ctx);
            ContentActor actor = await services.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            string id = RouteHelper.Param(ctx, "id");
            string key = RouteHelper.Param(ctx, "key");
            object state = add
                ? await services.Reactions.AddAsync(actor, type, id, key, ctx.Token).ConfigureAwait(false)
                : await services.Reactions.RemoveAsync(actor, type, id, key, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, state).ConfigureAwait(false);
        }

        private static async Task ReactionsAsync(HttpContextBase ctx, PropertyDirectory directory, TargetTypeEnum type, EnumerationQuery query)
        {
            ApplicationServices services = directory.For(ctx);
            ContentActor actor = await services.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await services.Reactions.ListAsync(actor, type, RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
