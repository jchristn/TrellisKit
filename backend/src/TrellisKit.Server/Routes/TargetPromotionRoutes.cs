namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.Routing;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Promote-to-feed routes of a content target provider type (docs/PHASE4_CONTRACT.md section 5.8):
    /// POST {prefix}/{id}/promote and POST {prefix}/{id}/unpromote, for example under /api/v1/lessons (Lesson) and
    /// /api/v1/events (Event).
    /// </summary>
    public static class TargetPromotionRoutes
    {
        #region Public-Methods

        /// <summary>
        /// Register POST {prefix}/{id}/promote and POST {prefix}/{id}/unpromote with OpenAPI documentation.
        /// </summary>
        /// <param name="post">Post-authentication routing group.</param>
        /// <param name="directory">Property directory.</param>
        /// <param name="prefix">Collection path, for example /api/v1/lessons (no trailing slash).</param>
        /// <param name="type">Target type (Lesson or Event).</param>
        /// <param name="tag">OpenAPI tag.</param>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        public static void Register(RoutingGroup post, PropertyDirectory directory, string prefix, TargetTypeEnum type, string tag)
        {
            ArgumentNullException.ThrowIfNull(post);
            ArgumentNullException.ThrowIfNull(directory);
            if (String.IsNullOrWhiteSpace(prefix)) throw new ArgumentNullException(nameof(prefix));
            if (String.IsNullOrWhiteSpace(tag)) throw new ArgumentNullException(nameof(tag));
            string root = prefix.TrimEnd('/') + "/{id}";
            string kind = type == TargetTypeEnum.Event ? "PromotedEvent" : "PromotedLesson";
            string notifyDefault = type == TargetTypeEnum.Event ? "false" : "true";
            post.Parameter.Add(HttpMethod.POST, root + "/promote", ctx => PromoteAsync(ctx, directory, type), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Promote " + type + " to the feed", tag, "Body { title?, excerpt?, pin? (SystemAdmin only), audience? (may only narrow the source's), notifyMembers? (default " + notifyDefault + ": a NewFeedPost fan-out) }. " +
                    "SystemAdmin or members with canPromoteToFeed who can read the " + type + ". Creates a " + kind + " feed card { source { type, id, title }, commentTarget } whose conversation is the " + type + ". " +
                    "Hiding, removing, unpublishing, or canceling the source hides the card. One card per " + type + ".")
                    .PathParam("id", type + " identifier").Body("PromoteRequest", false).Returns("FeedPost", 201).Resp(400, "ValidationFailed").Resp(403, "PromotionNotAllowed").Resp(404, "NotFound")
                    .Resp(409, "AlreadyPromoted or PinLimitReached").Resp(429, "RateLimited (forums.promotionsPerHour)"));
            post.Parameter.Add(HttpMethod.POST, root + "/unpromote", ctx => UnpromoteAsync(ctx, directory, type), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("End the promotion of " + type, tag, "SystemAdmin, the member the card is attributed to, or a moderator of the source's scope. Deletes the feed card. Idempotent.")
                    .PathParam("id", type + " identifier").Returns("{ targetType, targetId, unpromoted, feedPostId }").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(429, "RateLimited"));
        }

        #endregion

        #region Private-Methods

        private static async Task PromoteAsync(HttpContextBase ctx, PropertyDirectory directory, TargetTypeEnum type)
        {
            PromoteRequest request = RouteHelper.ReadJson<PromoteRequest>(ctx);
            ApplicationServices services = directory.For(ctx);
            ContentActor actor = await services.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            RequestInfo info = RequestInfo.From(ctx, services.Settings.Rest);
            await RouteHelper.SendJsonAsync(ctx, 201, await services.Forums.Promotions.PromoteTargetAsync(actor, info, type, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private static async Task UnpromoteAsync(HttpContextBase ctx, PropertyDirectory directory, TargetTypeEnum type)
        {
            ApplicationServices services = directory.For(ctx);
            ContentActor actor = await services.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            RequestInfo info = RequestInfo.From(ctx, services.Settings.Rest);
            await RouteHelper.SendJsonAsync(ctx, 200, await services.Forums.Promotions.UnpromoteTargetAsync(actor, info, type, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
