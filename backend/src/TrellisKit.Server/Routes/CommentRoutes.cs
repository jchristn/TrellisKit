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
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Threaded comment routes (Phase 2 contract section 7, docs/ENUMERATION_AND_METADATA.md): tree pages, children,
    /// permalink, create, edit, delete, and revisions. Members only.
    /// </summary>
    public class CommentRoutes
    {
        #region Private-Members

        private const string _Tag = "Comments";
        private const string _Item = "Comment (with children and moreChildren)";
        private const string _RevisionItem = "{ id, revision, body, attachmentSummary, createdUtc, labels, tags }";
        private const string _Shape = "depth, childLimit";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public CommentRoutes(PropertyDirectory directory)
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
            string feed = Constants.ApiPrefix + "/feed/{id}/comments";
            string comments = Constants.ApiPrefix + "/comments/{id}";
            string tree = "Top-level comments with replies nested up to depth levels (filter depth 1 to 5, default 3) and childLimit replies each (filter childLimit 1 to 20, default 5); " +
                "the result adds commentCount (visible comments on the post). Orderings Top (default: score desc, createdUtc asc, id), CreatedDescending, CreatedAscending, used at every level. " +
                "moreChildren { remainingCount, cursor, continuationToken, enumerate } continues a comment's replies through POST /api/v1/comments/{id}/children/enumerate (cursor null: from the first reply). " +
                "Placeholders (Deleted, Removed, Hidden, BlockedAuthor) appear only with visible descendants. maxResults default 20, max 50. Members only.";
            post.Parameter.Add(HttpMethod.GET, feed, TreeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List a post's comment tree", _Tag, tree)
                    .PathParam("id", "Post identifier").Enumeration(_Item, ContentCollections.CommentOrderings, _Shape).Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.POST, feed + "/enumerate", EnumerateTreeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate a post's comment tree", _Tag, "EnumerationQuery body (filters.depth, filters.childLimit); same result as the GET form. " + tree)
                    .PathParam("id", "Post identifier").Body("EnumerationQuery", false).Enumeration(_Item, ContentCollections.CommentOrderings, _Shape, false)
                    .Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.POST, feed, CreateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Comment or reply", _Tag, "Body: body, parentCommentId, mediaIds (at most comments.maxAttachments), showLinkPreview, labels, tags. A reply to a comment at comments.maxDepth is stored as its sibling with replyToCommentId. Members with canPost who are not muted.")
                    .PathParam("id", "Post identifier").Body("CommentRequest").Returns("Comment", 201).Resp(400, "ValidationFailed, MediaLimitExceeded, or ContentFiltered")
                    .Resp(403, "PostingNotAllowed, MediaNotAllowed, VideoUploadNotAllowed, Muted, or InteractionBlocked").Resp(404, "NotFound")
                    .Resp(409, "CommentsDisabled, ParentUnavailable, or MediaNotAttachable").Resp(429, "RateLimited (comments per minute and day)"));
            post.Parameter.Add(HttpMethod.GET, comments, PermalinkAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a comment in context", _Tag, "{ target, ancestors (root first, without children; at most comments.maxDepth), comment (with children) }. Query depth (1 to 5, default 3, counting the comment), childLimit (1 to 20, default 5), ordering (Top, CreatedDescending, CreatedAscending). Members only.")
                    .PathParam("id", "Comment identifier").QueryParam("depth", "Levels (counting the comment).").QueryParam("childLimit", "Replies per comment.").QueryParam("ordering", "Sibling order.")
                    .Bounded("CommentPermalink", "ancestors at most comments.maxDepth (20); children limited by depth and childLimit").Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(429, "RateLimited"));
            string children = "The direct replies of a comment with the same nesting rules. continuationToken (alias cursor, from moreChildren) continues after the last reply shown; use the ordering it was issued for. Members only.";
            post.Parameter.Add(HttpMethod.GET, comments + "/children", ChildrenAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List a comment's replies", _Tag, children)
                    .PathParam("id", "Comment identifier").QueryParam("cursor", "Alias of continuationToken.").Enumeration(_Item, ContentCollections.CommentOrderings, _Shape)
                    .Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.POST, comments + "/children/enumerate", EnumerateChildrenAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate a comment's replies", _Tag, "EnumerationQuery body plus cursor (alias of continuationToken). " + children)
                    .PathParam("id", "Comment identifier").Body("EnumerationQuery plus cursor", false).Enumeration(_Item, ContentCollections.CommentOrderings, _Shape, false)
                    .Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.PUT, comments, UpdateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit a comment", _Tag, "Author only (with canPost). Body: body, mediaIds, showLinkPreview, labels, tags. Edits after content.editGraceSeconds store a revision.")
                    .PathParam("id", "Comment identifier").Body("CommentRequest").Returns("Comment").Resp(400, "ValidationFailed, MediaLimitExceeded, or ContentFiltered")
                    .Resp(403, "Forbidden, PostingNotAllowed, MediaNotAllowed, VideoUploadNotAllowed, or Muted").Resp(404, "NotFound").Resp(409, "MediaNotAttachable").Resp(429, "RateLimited (rateLimits.editsPerHour)"));
            post.Parameter.Add(HttpMethod.DELETE, comments, DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a comment", _Tag, "Author or SystemAdmin. Soft delete; the comment stays as a Deleted placeholder while it has visible replies.")
                    .PathParam("id", "Comment identifier").Resp(204, "Deleted").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.GET, comments + "/revisions", RevisionsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List a comment's revisions", _Tag, "Newest first; visible to everyone who can see the comment. Labels and tags are those of each version (type CommentRevision) and filter the list. maxResults default 20, max 50. Members only.")
                    .PathParam("id", "Comment identifier").Enumeration(_RevisionItem, "CreatedDescending, CreatedAscending").Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, comments + "/revisions/enumerate", EnumerateRevisionsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate a comment's revisions", _Tag, "EnumerationQuery body; same result as the GET form. Members only.")
                    .PathParam("id", "Comment identifier").Body("EnumerationQuery", false).Enumeration(_RevisionItem, "CreatedDescending, CreatedAscending", String.Empty, false)
                    .Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private Task TreeAsync(HttpContextBase ctx)
        {
            return SendTreeAsync(ctx, RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx));
        }

        private Task EnumerateTreeAsync(HttpContextBase ctx)
        {
            return SendTreeAsync(ctx, RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx));
        }

        private async Task SendTreeAsync(HttpContextBase ctx, EnumerationQuery query)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Comments.TreeAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private Task ChildrenAsync(HttpContextBase ctx)
        {
            return SendChildrenAsync(ctx, RouteHelper.ReadEnumerationQuery<CommentChildrenQuery>(ctx));
        }

        private Task EnumerateChildrenAsync(HttpContextBase ctx)
        {
            return SendChildrenAsync(ctx, RouteHelper.ReadEnumerationBody<CommentChildrenQuery>(ctx));
        }

        private async Task SendChildrenAsync(HttpContextBase ctx, CommentChildrenQuery query)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Comments.ChildrenAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PermalinkAsync(HttpContextBase ctx)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            int? depth = RouteHelper.QueryInt(ctx, "depth", errors);
            int? childLimit = RouteHelper.QueryInt(ctx, "childLimit", errors);
            string? ordering = RouteHelper.Query(ctx, "ordering") ?? RouteHelper.Query(ctx, "sort");
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Comments.PermalinkAsync(actor, RouteHelper.Param(ctx, "id"), depth, childLimit, ordering, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            CommentRequest request = RouteHelper.ReadJson<CommentRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Comments.CreateAsync(actor, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            CommentRequest request = RouteHelper.ReadJson<CommentRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Comments.UpdateAsync(actor, RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await _Directory.For(ctx).Comments.DeleteAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
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
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Comments.ListRevisionsAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
