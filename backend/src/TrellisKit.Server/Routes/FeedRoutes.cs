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
    /// Member feed routes (Phase 2 contract section 5, docs/ENUMERATION_AND_METADATA.md): the feed, read, create, edit,
    /// delete, pin and unpin, revisions, and the caller's posts.
    /// </summary>
    public class FeedRoutes
    {
        #region Private-Members

        private const string _Tag = "Feed";
        private const string _Item = "FeedPost";
        private const string _RevisionItem = "{ id, revision, title, body, attachmentSummary, editedBy: UserSummary, createdUtc, labels, tags }";
        private const string _RevisionOrderings = "CreatedDescending, CreatedAscending";
        private const string _Base = Constants.ApiPrefix + "/feed";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public FeedRoutes(PropertyDirectory directory)
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
            string feedDescription = "Published posts. Ordering Feed (default) sorts pinned posts first (pinnedUtc descending), then unpinned posts newest first. Both groups share one keyset and count toward maxResults; pins may span pages and no page exceeds its cap. " +
                "PublishedDescending has no pinned prefix. Posts by members you blocked and posts whose audience you do not meet are omitted; filter audience narrows the list. Search covers title and body. maxResults default 20, max 50. Members only.";
            post.Static.Add(HttpMethod.GET, _Base, ListAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List the feed", _Tag, feedDescription)
                    .Enumeration(_Item, ContentCollections.FeedOrderings, "publishedAfterUtc, publishedBeforeUtc, audience").Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(429, "RateLimited"));
            post.Static.Add(HttpMethod.POST, _Base + "/enumerate", EnumerateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate the feed", _Tag, "EnumerationQuery body; same result as the GET form. " + feedDescription)
                    .Body("EnumerationQuery", false).Enumeration(_Item, ContentCollections.FeedOrderings, "publishedAfterUtc, publishedBeforeUtc, audience", false)
                    .Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(429, "RateLimited"));
            post.Static.Add(HttpMethod.POST, _Base, CreateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a feed post", _Tag, "SystemAdmin, or members with canPostToFeed and canPost. Body: title, body, mediaIds, visibility (Public needs SystemAdmin), audience, commentsEnabled, publishAtUtc, pin (SystemAdmin), showLinkPreview, labels, tags.")
                    .Body("FeedPostRequest").Returns(_Item, 201).Resp(400, "ValidationFailed, MediaLimitExceeded, or ContentFiltered")
                    .Resp(403, "Forbidden, PostingNotAllowed, MediaNotAllowed, VideoUploadNotAllowed, or Muted").Resp(409, "MediaNotAttachable or PinLimitReached").Resp(429, "RateLimited (feed.postsPerHour)"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a feed post", _Tag, "Authors see their posts in every state; SystemAdmin every state; FeedModerators Hidden and Removed posts. Others see Published posts whose audience they meet (404 otherwise). Members only.")
                    .PathParam("id", "Post identifier").Returns(_Item).Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}", UpdateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit a feed post", _Tag, "Author (with canPost) in any state, or SystemAdmin. Omitted fields stay. Edits after content.editGraceSeconds that change title, body, or attachments store a revision. publishAtUtc only while Scheduled (null publishes now).")
                    .PathParam("id", "Post identifier").Body("FeedPostRequest").Returns(_Item).Resp(400, "ValidationFailed, MediaLimitExceeded, or ContentFiltered")
                    .Resp(403, "Forbidden, PostingNotAllowed, MediaNotAllowed, VideoUploadNotAllowed, or Muted").Resp(404, "NotFound").Resp(409, "MediaNotAttachable").Resp(429, "RateLimited (rateLimits.editsPerHour)"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}", DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a feed post", _Tag, "Author or SystemAdmin. Soft delete; media is deleted after moderation.retainRemovedDays.")
                    .PathParam("id", "Post identifier").Resp(204, "Deleted").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/pin", PinAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Pin a feed post", _Tag, "SystemAdmin. Published posts only; idempotent.")
                    .PathParam("id", "Post identifier").Returns(_Item).Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "PinLimitReached or NotPublished"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/unpin", UnpinAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Unpin a feed post", _Tag, "SystemAdmin. Idempotent.")
                    .PathParam("id", "Post identifier").Returns(_Item).Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}/revisions", RevisionsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List a feed post's revisions", _Tag, "Newest first; visible to everyone who can see the post. Labels and tags are those of each version (type FeedPostRevision) and filter the list. maxResults default 20, max 50. Members only.")
                    .PathParam("id", "Post identifier").Enumeration(_RevisionItem, _RevisionOrderings).Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/revisions/enumerate", EnumerateRevisionsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate a feed post's revisions", _Tag, "EnumerationQuery body; same result as the GET form. Members only.")
                    .PathParam("id", "Post identifier").Body("EnumerationQuery", false).Enumeration(_RevisionItem, _RevisionOrderings, String.Empty, false)
                    .Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound"));
            post.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/me/feed-posts", MineAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List your feed posts", _Tag, "Your posts in every state except Deleted, newest first. Filters state (Scheduled, Processing, Published, Hidden, Removed) and audience. Signed in.")
                    .Enumeration(_Item, "CreatedDescending, CreatedAscending", "state").Resp(400, "ValidationFailed or InvalidContinuationToken"));
            post.Static.Add(HttpMethod.POST, Constants.ApiPrefix + "/me/feed-posts/enumerate", EnumerateMineAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate your feed posts", _Tag, "EnumerationQuery body; same result as the GET form. Signed in.")
                    .Body("EnumerationQuery", false).Enumeration(_Item, "CreatedDescending, CreatedAscending", "state", false).Resp(400, "ValidationFailed or InvalidContinuationToken"));
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private Task ListAsync(HttpContextBase ctx)
        {
            return SendFeedAsync(ctx, RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx));
        }

        private Task EnumerateAsync(HttpContextBase ctx)
        {
            return SendFeedAsync(ctx, RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx));
        }

        private async Task SendFeedAsync(HttpContextBase ctx, EnumerationQuery query)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Feed.ListAsync(actor, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            FeedPostRequest request = RouteHelper.ReadJson<FeedPostRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Feed.CreateAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Feed.GetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            FeedPostRequest request = RouteHelper.ReadJson<FeedPostRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Feed.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await _Directory.For(ctx).Feed.DeleteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task PinAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Feed.SetPinnedAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), true, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UnpinAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Feed.SetPinnedAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), false, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private Task RevisionsAsync(HttpContextBase ctx)
        {
            return SendRevisionsAsync(ctx, RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx));
        }

        private Task EnumerateRevisionsAsync(HttpContextBase ctx)
        {
            return SendRevisionsAsync(ctx, RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx));
        }

        private async Task SendRevisionsAsync(HttpContextBase ctx, EnumerationQuery query)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Feed.ListRevisionsAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private Task MineAsync(HttpContextBase ctx)
        {
            return SendMineAsync(ctx, RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx));
        }

        private Task EnumerateMineAsync(HttpContextBase ctx)
        {
            return SendMineAsync(ctx, RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx));
        }

        private async Task SendMineAsync(HttpContextBase ctx, EnumerationQuery query)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Feed.ListMineAsync(actor, query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
