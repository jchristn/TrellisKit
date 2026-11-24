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
    /// Member blocks (Phase 2 contract section 9.2): block and unblock (Member) and the caller's block list (Signed in).
    /// </summary>
    public class BlockRoutes
    {
        #region Private-Members

        private const string _Tag = "Blocks";
        private const string _Orderings = "CreatedDescending, CreatedAscending";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public BlockRoutes(PropertyDirectory directory)
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
            post.Parameter.Add(HttpMethod.POST, Constants.ApiPrefix + "/users/{id}/block", BlockAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Block a member", _Tag, "Idempotent. Blocks are private. Muted and restricted members may block. Members only.")
                    .PathParam("id", "Member identifier").Returns("{ blocked: true }").Resp(400, "CannotBlockSelf or CannotBlockStaff").Resp(404, "NotFound")
                    .Resp(409, "BlockLimitReached (more than 1000 blocks)").Resp(429, "RateLimited (moderation.blocksPerHour, shared with unblock)"));
            post.Parameter.Add(HttpMethod.DELETE, Constants.ApiPrefix + "/users/{id}/block", UnblockAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Unblock a member", _Tag, "Idempotent. Members only.")
                    .PathParam("id", "Member identifier").Returns("{ blocked: false }").Resp(429, "RateLimited"));
            post.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/me/blocks", ListAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List the members you blocked", _Tag, "Newest first by default; search covers the blocked member's display name and handle; label and tag filters apply to the blocked member. Signed in.")
                    .Enumeration("{ user: UserSummary, createdUtc, labels, tags }", _Orderings).Resp(400, "ValidationFailed or InvalidContinuationToken"));
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/me/blocks/enumerate", EnumerateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate the members you blocked", _Tag, "EnumerationQuery body; same result as the GET form. Signed in.")
                    .Body("EnumerationQuery", false).Enumeration("{ user: UserSummary, createdUtc, labels, tags }", _Orderings, withQueryParameters: false).Resp(400, "ValidationFailed or InvalidContinuationToken"));
        }

        #endregion

        #region Private-Methods

        private async Task BlockAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Blocks.BlockAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UnblockAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Blocks.UnblockAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private Task ListAsync(HttpContextBase ctx)
        {
            return SendPageAsync(ctx, RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx));
        }

        private Task EnumerateAsync(HttpContextBase ctx)
        {
            return SendPageAsync(ctx, RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx));
        }

        private async Task SendPageAsync(HttpContextBase ctx, EnumerationQuery query)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Blocks.ListAsync(actor, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
