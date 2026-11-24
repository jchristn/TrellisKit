namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Threaded comments (contract section 7): tree pages (one query per level with ROW_NUMBER() per parent), children
    /// continuation ("load more replies" and "continue this thread"), permalinks with ancestors, create (with the
    /// max-depth flattening of section 7.1), edit with revision history, soft delete, and the moderation hooks.
    /// Placeholders (Deleted, Removed, Hidden, BlockedAuthor) appear only when they have visible descendants. The
    /// moreChildren cursor is a continuation token of POST /api/v1/comments/{id}/children/enumerate fingerprinted on the
    /// parent and the ordering only (depth and childLimit are not part of it), so it is accepted whatever page shape
    /// the client asks for. Thread safety: safe to call concurrently; counters are recomputed in the write batch.
    /// </summary>
    public class CommentService
    {
        #region Public-Members

        /// <summary>
        /// Default levels of a tree page (counting the top level as 1).
        /// </summary>
        public const int DefaultDepth = 3;

        /// <summary>
        /// Most levels of a tree page.
        /// </summary>
        public const int MaxDepthLevels = 5;

        /// <summary>
        /// Default replies under each returned comment.
        /// </summary>
        public const int DefaultChildLimit = 5;

        /// <summary>
        /// Most replies under each returned comment.
        /// </summary>
        public const int MaxChildLimit = 20;

        /// <summary>
        /// Default top-level comments per page.
        /// </summary>
        public const int DefaultPageSize = 20;

        /// <summary>
        /// Most top-level comments per page.
        /// </summary>
        public const int MaxPageSize = 50;

        /// <summary>
        /// Most comments in one tree response, counting every level (top-level comments, replies, and the permalink
        /// subtree). Without it the per-level limits multiply (50 x 20 per level to depth 5). Parents whose replies were
        /// left out still carry moreChildren, so clients continue on the children route.
        /// </summary>
        public const int MaxTreeNodes = 1000;

        /// <summary>
        /// The comment budget of one tree response, 1 to <see cref="MaxTreeNodes"/>. Default <see cref="MaxTreeNodes"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is outside 1 to MaxTreeNodes.</exception>
        public int TreeNodeLimit
        {
            get { return _TreeNodeLimit; }
            set
            {
                if (value < 1 || value > MaxTreeNodes) throw new ArgumentOutOfRangeException(nameof(value), "Use 1 to " + MaxTreeNodes + ".");
                _TreeNodeLimit = value;
            }
        }

        /// <summary>
        /// Resolves a forum thread conversation for reads (forWrite false) or new comments (forWrite true), applying the
        /// thread rules (Phase 3 section 4.10). Set by the forum module; null means thread comments are unavailable.
        /// </summary>
        public Func<ContentActor, string, bool, CancellationToken, Task<CommentHost>>? ThreadHostResolver { get; set; } = null;

        /// <summary>
        /// Called after a comment is created on a thread host, or on a feed post (for its discussion thread followers).
        /// </summary>
        public Func<CommentHost, Comment, Comment?, CancellationToken, Task>? AfterCreate { get; set; } = null;

        /// <summary>
        /// Called after an edited thread comment's new mentions are stored, to notify them with the thread context.
        /// </summary>
        public Func<Comment, CancellationToken, Task>? ThreadMentions { get; set; } = null;

        /// <summary>
        /// Resolves an idea conversation for reads (forWrite false) or new comments (forWrite true) (set by the ideas
        /// module; docs/PHASE5_CONTRACT.md section 7.1); null means idea comments are unavailable.
        /// </summary>
        public Func<ContentActor, string, bool, CancellationToken, Task<CommentHost>>? IdeaHostResolver { get; set; } = null;

        /// <summary>
        /// Called after a comment is created on an idea (the ideas module sends PostComment, CommentReply, and Mention).
        /// </summary>
        public Func<CommentHost, Comment, Comment?, CancellationToken, Task>? IdeaAfterCreate { get; set; } = null;

        #endregion

        #region Private-Members

        private int _TreeNodeLimit = MaxTreeNodes;

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="services">Application services (read on each call).</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public CommentService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// GET /api/v1/feed/{id}/comments: a tree page of top-level comments (ContentCollections.CommentTree) with
        /// replies nested up to depth levels and childLimit replies per comment.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="postId">Post identifier.</param>
        /// <param name="query">Query (filters depth and childLimit shape the page).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tree page with the post's commentCount.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotFound, 429 RateLimited.</exception>
        public async Task<CommentTreePage> TreeAsync(ContentActor actor, string postId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            FeedPost post = await _Services.ContentAccess.RequireVisiblePostAsync(actor, postId, token).ConfigureAwait(false);
            RequireOwnConversation(post);
            return await TreeCoreAsync(actor, FeedHost(post, actor), query, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/threads/{id}/comments/enumerate: a thread's comment tree page (Phase 3 section 4.10), with
        /// acceptedAnswerId for Question threads.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="threadId">Thread.</param>
        /// <param name="query">Query (filters depth and childLimit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tree page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound, 409 UseCommentTarget.</exception>
        public async Task<CommentTreePage> ThreadTreeAsync(ContentActor actor, string threadId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            if (ThreadHostResolver == null) throw ApiErrorException.NotFound("Thread");
            CommentHost host = await ThreadHostResolver(actor, threadId, false, token).ConfigureAwait(false);
            CommentTreePage page = await TreeCoreAsync(actor, host, query, token).ConfigureAwait(false);
            page.AcceptedAnswerId = host.AcceptedAnswerId;
            return page;
        }

        /// <summary>
        /// POST {prefix}/{id}/comments/enumerate: the comment tree of a content target provider object (Event, Lesson;
        /// docs/PHASE4_CONTRACT.md section 6). The provider resolves the host (404 when the caller cannot read it);
        /// moderators of the type's comments see hidden and removed comments.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetType">Host type.</param>
        /// <param name="targetId">Host identifier.</param>
        /// <param name="query">Enumeration query (filters depth, childLimit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tree page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 or 404.</exception>
        public async Task<CommentTreePage> TargetTreeAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            CommentHost host = await ProviderHostAsync(actor, targetType, targetId, false, token).ConfigureAwait(false);
            return await TreeCoreAsync(actor, host, query, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST {prefix}/{id}/comments: comment or reply on a content target provider object. The provider applies the
        /// write rules (gating, commentsEnabled, canceled events) and is told about the comment
        /// (<see cref="ContentTargetProvider.AfterCommentAsync"/>); mentions notify with the object's path.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetType">Host type.</param>
        /// <param name="targetId">Host identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The comment.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400, 403, 404, 409, or 429 per the contracts.</exception>
        public async Task<CommentView> CreateOnTargetAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, CommentRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            string userId = actor.RequireUserId();
            using (Activity? activity = TrellisKitTelemetry.StartSpan("comments.create"))
            {
                try
                {
                    CommentHost host = await ProviderHostAsync(actor, targetType, targetId, true, token).ConfigureAwait(false);
                    CommentView view = await CreateCoreAsync(actor, userId, host, null, request, token).ConfigureAwait(false);
                    await new Metrics.ProductMetricEventService(_Services).ObserveAsync("member.firstAction", userId, token).ConfigureAwait(false);
                    Count("create", "success", view.Depth);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return view;
                }
                catch (ApiErrorException)
                {
                    Count("create", "rejected", 0);
                    throw;
                }
            }
        }

        /// <summary>
        /// POST /api/v1/threads/{id}/comments: comment or reply on a thread (Phase 3 section 4.10).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="threadId">Thread.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The comment.</returns>
        /// <exception cref="ApiErrorException">400, 403, 404, 409, or 429 per the contracts.</exception>
        public async Task<CommentView> CreateOnThreadAsync(ContentActor actor, string threadId, CommentRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            string userId = actor.RequireUserId();
            if (ThreadHostResolver == null) throw ApiErrorException.NotFound("Thread");
            using (Activity? activity = TrellisKitTelemetry.StartSpan("comments.create"))
            {
                try
                {
                    CommentHost host = await ThreadHostResolver(actor, threadId, true, token).ConfigureAwait(false);
                    CommentView view = await CreateCoreAsync(actor, userId, host, null, request, token).ConfigureAwait(false);
                    await new Metrics.ProductMetricEventService(_Services).ObserveAsync("member.firstAction", userId, token).ConfigureAwait(false);
                    Count("create", "success", view.Depth);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return view;
                }
                catch (ApiErrorException)
                {
                    Count("create", "rejected", 0);
                    throw;
                }
            }
        }

        /// <summary>
        /// POST /api/v1/ideas/{id}/comments/enumerate: an idea's comment tree page (Phase 2 shapes). Comments of a
        /// merged idea stay readable.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="ideaId">Idea.</param>
        /// <param name="query">Query (filters depth and childLimit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tree page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 401, 402, 404 NotFound.</exception>
        public async Task<CommentTreePage> IdeaTreeAsync(ContentActor actor, string ideaId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            if (IdeaHostResolver == null) throw ApiErrorException.NotFound("Idea");
            CommentHost host = await IdeaHostResolver(actor, ideaId, false, token).ConfigureAwait(false);
            return await TreeCoreAsync(actor, host, query, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/ideas/{id}/comments: comment or reply on an idea (409 IdeaMerged for a merged idea).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="ideaId">Idea.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The comment.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400, 403, 404, 409, or 429 per the contracts.</exception>
        public async Task<CommentView> CreateOnIdeaAsync(ContentActor actor, string ideaId, CommentRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            string userId = actor.RequireUserId();
            if (IdeaHostResolver == null) throw ApiErrorException.NotFound("Idea");
            using (Activity? activity = TrellisKitTelemetry.StartSpan("comments.create"))
            {
                try
                {
                    CommentHost host = await IdeaHostResolver(actor, ideaId, true, token).ConfigureAwait(false);
                    CommentView view = await CreateCoreAsync(actor, userId, host, null, request, token).ConfigureAwait(false);
                    await new Metrics.ProductMetricEventService(_Services).ObserveAsync("member.firstAction", userId, token).ConfigureAwait(false);
                    Count("create", "success", view.Depth);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return view;
                }
                catch (ApiErrorException)
                {
                    Count("create", "rejected", 0);
                    throw;
                }
            }
        }

        private async Task<CommentTreePage> TreeCoreAsync(ContentActor actor, CommentHost host, EnumerationQuery? query, CancellationToken token)
        {
            EnumerationQuery q = query ?? new EnumerationQuery();
            ReadShape(q, out int depth, out int childLimit);
            EnumerationQuery stripped = WithoutShape(q, null);
            if (stripped.MaxResults == null) stripped.MaxResults = DefaultPageSize;
            EnumerationRequest request = _Services.Enumeration.Prepare(stripped, ContentCollections.CommentTree, actor.IsStaffFor(TargetTypeEnum.Comment), "tree|" + host.TargetId, MaxPageSize);
            CommentViewerFilter viewer = await ViewerAsync(actor, token).ConfigureAwait(false);
            if (host.ModeratorInScope) viewer.IsModerator = true;
            EnumerationResult<Comment> page = await _Services.Database.Comments.EnumerateLevelAsync(host.TargetType, host.TargetId, null, request, viewer, token).ConfigureAwait(false);
            List<CommentView> views = await BuildTreeAsync(page.Objects, depth, childLimit, request.Ordering.Name, actor, viewer, token).ConfigureAwait(false);
            return CommentTreePage.From(page.With(views), host.CommentCount);
        }

        /// <summary>
        /// GET /api/v1/comments/{id}/children: the direct replies of a comment (ContentCollections.CommentChildren) with
        /// the same nesting rules; continuationToken (or its alias cursor, from moreChildren) continues after the last
        /// reply shown. A token must be used with the ordering it was issued for.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="commentId">Parent comment.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tree page with the post's commentCount.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotFound, 429 RateLimited.</exception>
        public async Task<CommentTreePage> ChildrenAsync(ContentActor actor, string commentId, CommentChildrenQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            Comment parent = await _Services.ContentAccess.RequireCommentAsync(actor, commentId, token).ConfigureAwait(false);
            CommentChildrenQuery q = query ?? new CommentChildrenQuery();
            if (String.IsNullOrWhiteSpace(q.ContinuationToken) && !String.IsNullOrWhiteSpace(q.Cursor)) q.ContinuationToken = q.Cursor;
            ReadShape(q, out int depth, out int childLimit);
            EnumerationQuery stripped = WithoutShape(q, null);
            if (stripped.MaxResults == null) stripped.MaxResults = DefaultPageSize;
            string ordering = CanonicalOrdering(stripped.Ordering);
            EnumerationRequest request = _Services.Enumeration.Prepare(stripped, ContentCollections.CommentChildren, actor.IsStaffFor(TargetTypeEnum.Comment), ChildrenScope(parent.Id, ordering), MaxPageSize);
            CommentViewerFilter viewer = await ViewerAsync(actor, token).ConfigureAwait(false);
            if (parent.TargetType == TargetTypeEnum.Thread && actor.ModeratesAnyForum) viewer.IsModerator = true;
            if (Provider(parent.TargetType) != null && actor.ModeratesCommentsOn(parent.TargetType)) viewer.IsModerator = true;
            EnumerationResult<Comment> page = await _Services.Database.Comments.EnumerateLevelAsync(parent.TargetType, parent.TargetId, parent.Id, request, viewer, token).ConfigureAwait(false);
            List<CommentView> views = await BuildTreeAsync(page.Objects, depth, childLimit, request.Ordering.Name, actor, viewer, token).ConfigureAwait(false);
            return CommentTreePage.From(page.With(views), await CommentCountAsync(parent, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/comments/{id}: the comment in context (permalink): its target, its ancestors root first
        /// (without children; bounded by comments.maxDepth), and the comment with replies nested up to depth levels
        /// (counting the comment as level 1).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="commentId">Comment identifier.</param>
        /// <param name="depth">Levels, or null for 3.</param>
        /// <param name="childLimit">Replies per comment, or null for 5.</param>
        /// <param name="ordering">Ordering (Top, CreatedDescending, CreatedAscending), or null for Top.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Permalink.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound, 429 RateLimited.</exception>
        public async Task<CommentPermalink> PermalinkAsync(ContentActor actor, string commentId, int? depth, int? childLimit, string? ordering, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            Comment comment = await _Services.ContentAccess.RequireCommentAsync(actor, commentId, token).ConfigureAwait(false);
            if (!ContentAccessService.CanSeeCommentContent(comment, actor) && comment.DescendantCount == 0) throw ApiErrorException.NotFound("Comment");
            Dictionary<string, string> errors = new Dictionary<string, string>();
            int levels = depth ?? DefaultDepth;
            int limit = childLimit ?? DefaultChildLimit;
            if (levels < 1 || levels > MaxDepthLevels) errors["depth"] = "Must be 1 to " + MaxDepthLevels + ".";
            if (limit < 1 || limit > MaxChildLimit) errors["childLimit"] = "Must be 1 to " + MaxChildLimit + ".";
            string order = CanonicalOrdering(ordering);
            if (!ContentCollections.CommentTree.Orderings.ContainsKey(order)) errors["ordering"] = "Use one of " + ContentCollections.CommentOrderings + ".";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            CommentViewerFilter viewer = await ViewerAsync(actor, token).ConfigureAwait(false);
            List<CommentView> tree = await BuildTreeAsync(new List<Comment> { comment }, levels, limit, order, actor, viewer, token).ConfigureAwait(false);
            // A permalink to a comment by a member the caller blocked is 404, not a BlockedAuthor placeholder.
            if (tree.Count == 0 || tree[0].State == CommentStateEnum.BlockedAuthor) throw ApiErrorException.NotFound("Comment");
            List<string> ancestorIds = comment.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => !String.Equals(s, comment.Id, StringComparison.Ordinal)).ToList();
            int bound = Math.Max(1, _Services.Settings.Comments.MaxDepth);
            if (ancestorIds.Count > bound) ancestorIds = ancestorIds.Skip(ancestorIds.Count - bound).ToList();
            Dictionary<string, Comment> ancestorRows = (await _Services.Database.Comments.ReadManyAsync(ancestorIds, token).ConfigureAwait(false)).ToDictionary(c => c.Id, StringComparer.Ordinal);
            List<Comment> ordered = ancestorIds.Where(ancestorRows.ContainsKey).Select(i => ancestorRows[i]).ToList();
            Dictionary<string, CommentView> ancestorViews = await _Services.ContentViews.CommentsAsync(ordered, actor, false, token).ConfigureAwait(false);
            return new CommentPermalink
            {
                Target = new ContentTarget { Type = comment.TargetType.ToString(), Id = comment.TargetId },
                Ancestors = ordered.Select(c => ancestorViews[c.Id]).ToList(),
                Comment = tree[0]
            };
        }

        /// <summary>
        /// POST /api/v1/feed/{id}/comments: comment on a Published post with commentsEnabled, or reply to a comment of
        /// the same post. A reply to a comment at comments.maxDepth is stored as its sibling with replyToCommentId set.
        /// Notifies the post author (PostComment) or the replied-to author (CommentReply) and mentioned members, one
        /// notification per recipient.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="postId">Post identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The comment (children empty).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, MediaLimitExceeded, or ContentFiltered; 403 Forbidden (admin token),
        /// PostingNotAllowed, MediaNotAllowed, VideoUploadNotAllowed, Muted, or InteractionBlocked; 404 NotFound; 409 CommentsDisabled,
        /// ParentUnavailable, or MediaNotAttachable; 429 RateLimited.</exception>
        public async Task<CommentView> CreateAsync(ContentActor actor, string postId, CommentRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            string userId = actor.RequireUserId();
            using (Activity? activity = TrellisKitTelemetry.StartSpan("comments.create"))
            {
                try
                {
                    FeedPost post = await _Services.ContentAccess.RequireVisiblePostAsync(actor, postId, token).ConfigureAwait(false);
                    RequireOwnConversation(post);
                    CommentView view = await CreateCoreAsync(actor, userId, FeedHost(post, actor), post, request, token).ConfigureAwait(false);
                    await new Metrics.ProductMetricEventService(_Services).ObserveAsync("member.firstAction", userId, token).ConfigureAwait(false);
                    Count("create", "success", view.Depth);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return view;
                }
                catch (ApiErrorException)
                {
                    Count("create", "rejected", 0);
                    throw;
                }
            }
        }

        /// <summary>
        /// PUT /api/v1/comments/{id} (author only, who must still have canPost): edit body, media, or showLinkPreview.
        /// Edits within content.editGraceSeconds of creation are silent; later edits store a revision and set editedUtc.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="commentId">Comment identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Top-level fields present in the body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The comment.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, MediaLimitExceeded, or ContentFiltered; 403 Forbidden, PostingNotAllowed,
        /// MediaNotAllowed, VideoUploadNotAllowed, or Muted; 404 NotFound; 409 MediaNotAttachable; 429 RateLimited.</exception>
        public async Task<CommentView> UpdateAsync(ContentActor actor, string commentId, CommentRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            using (Activity? activity = TrellisKitTelemetry.StartSpan("comments.update"))
            {
                try
                {
                    CommentView view = await UpdateCoreAsync(actor, commentId, request, present, token).ConfigureAwait(false);
                    Count("update", "success", view.Depth);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return view;
                }
                catch (ApiErrorException)
                {
                    Count("update", "rejected", 0);
                    throw;
                }
            }
        }

        /// <summary>
        /// DELETE /api/v1/comments/{id} (author or SystemAdmin): soft delete. Counts of the post and ancestors drop, the
        /// node becomes a Deleted placeholder while it has visible replies, and its revisions, mentions, labels, and
        /// tags are deleted. Attached media stays for moderation.retainRemovedDays (the moderation feature's purge
        /// worker deletes it).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="commentId">Comment identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task DeleteAsync(ContentActor actor, string commentId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            Comment comment = await _Services.ContentAccess.RequireCommentAsync(actor, commentId, token).ConfigureAwait(false);
            if (comment.DeletedUtc != null) throw ApiErrorException.NotFound("Comment");
            bool isAuthor = actor.UserId != null && String.Equals(comment.AuthorUserId, actor.UserId, StringComparison.Ordinal);
            if (!isAuthor && !actor.IsSystemAdmin)
            {
                if (ContentAccessService.CanSeeCommentContent(comment, actor)) throw ApiErrorException.Forbidden("Only the author or a SystemAdmin can delete this comment.");
                throw ApiErrorException.NotFound("Comment");
            }
            using (Activity? activity = TrellisKitTelemetry.StartSpan("comments.delete"))
            {
                await _Services.Database.Comments.DeleteAsync(comment.Id, isAuthor ? CommentDeletedReasonEnum.Author : CommentDeletedReasonEnum.Admin, false, DateTime.UtcNow, token).ConfigureAwait(false);
                await ChangedAsync(comment.Id, token).ConfigureAwait(false);
                Count("delete", "success", comment.Depth);
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
        }

        /// <summary>
        /// GET /api/v1/comments/{id}/revisions: the comment's revisions, newest first (default 20, max 50), visible to
        /// everyone who can see the comment (placeholders have none).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="commentId">Comment identifier.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotFound, 429 RateLimited.</exception>
        public async Task<EnumerationResult<CommentRevisionView>> ListRevisionsAsync(ContentActor actor, string commentId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            Comment comment = await _Services.ContentAccess.RequireCommentAsync(actor, commentId, token).ConfigureAwait(false);
            if (!ContentAccessService.CanSeeCommentContent(comment, actor)) throw ApiErrorException.NotFound("Comment");
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (q.MaxResults == null) q.MaxResults = 20;
            bool staff = actor.IsStaffFor(TargetTypeEnum.CommentRevision);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ContentCollections.CommentRevisions, staff, "revisions|" + comment.Id, 50);
            EnumerationResult<CommentRevision> page = await _Services.Database.Comments.EnumerateRevisionsAsync(comment.Id, request, token).ConfigureAwait(false);
            return page.With(await _Services.Metadata.AttachAsync(TargetTypeEnum.CommentRevision, page.Objects.Select(ToView).ToList(), v => v.Id, staff, token).ConfigureAwait(false));
        }

        #region Moderation-Hooks

        /// <summary>
        /// Moderation hook: set a comment's moderation state (None, Hidden, Removed) with moderatedUtc,
        /// moderatedByUserId, and moderationReason, recomputing childCount, descendantCount, and score of every ancestor
        /// and the post's commentCount in the same transaction. The caller (moderation feature) checks scope, writes the
        /// moderation action, retains or restores media, and notifies.
        /// </summary>
        /// <param name="commentId">Comment identifier.</param>
        /// <param name="state">New state.</param>
        /// <param name="moderatorUserId">Moderator, or null for the system (auto-hide).</param>
        /// <param name="reason">Reason, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored comment.</returns>
        /// <exception cref="ApiErrorException">404 NotFound (unknown or deleted).</exception>
        public async Task<Comment> SetModerationStateAsync(string commentId, ModerationStateEnum state, string? moderatorUserId, string? reason, CancellationToken token = default)
        {
            Comment? existing = String.IsNullOrWhiteSpace(commentId) ? null : await _Services.Database.Comments.ReadAsync(commentId, token).ConfigureAwait(false);
            if (existing == null || existing.DeletedUtc != null) throw ApiErrorException.NotFound("Comment");
            Comment? stored = await _Services.Database.Comments.SetModerationStateAsync(existing.Id, state, moderatorUserId, reason, DateTime.UtcNow, token).ConfigureAwait(false);
            if (stored == null) throw ApiErrorException.NotFound("Comment");
            ContentTargetProvider? provider = Provider(stored.TargetType);
            if (provider != null) await provider.CommentChangedAsync(stored, token).ConfigureAwait(false);
            return stored;
        }

        /// <summary>
        /// Moderation hook: the Comment payload for a moderator (full content in any state, without children, with
        /// moderation: { state, reason, actedUtc } always present). Erased comments have no body.
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="commentId">Comment identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payload.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<CommentView> ModeratorViewAsync(ContentActor actor, string commentId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            Comment? comment = String.IsNullOrWhiteSpace(commentId) ? null : await _Services.Database.Comments.ReadAsync(commentId, token).ConfigureAwait(false);
            if (comment == null) throw ApiErrorException.NotFound("Comment");
            Dictionary<string, CommentView> views = await _Services.ContentViews.CommentsAsync(new[] { comment }, actor, true, token).ConfigureAwait(false);
            return views[comment.Id];
        }

        #endregion

        /// <summary>
        /// Telemetry depth bucket of a comment depth: 0, 1-3, or 4+.
        /// </summary>
        /// <param name="depth">Depth.</param>
        /// <returns>Bucket.</returns>
        public static string DepthBucket(int depth)
        {
            if (depth <= 0) return "0";
            return depth <= 3 ? "1-3" : "4+";
        }

        #endregion

        #region Private-Methods

        private ContentTargetProvider? Provider(TargetTypeEnum targetType)
        {
            if (targetType == TargetTypeEnum.FeedPost || targetType == TargetTypeEnum.Thread) return null;
            return _Services.ContentTargets.Find(targetType);
        }

        private async Task<CommentHost> ProviderHostAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, bool forWrite, CancellationToken token)
        {
            ContentTargetProvider provider = Provider(targetType) ?? throw ApiErrorException.NotFound(targetType.ToString());
            if (String.IsNullOrWhiteSpace(targetId)) throw ApiErrorException.NotFound(targetType.ToString());
            CommentHost host = await provider.CommentHostAsync(actor, targetType, targetId.Trim(), forWrite, token).ConfigureAwait(false);
            if (host.TargetType != targetType || String.IsNullOrEmpty(host.TargetId)) throw new InvalidOperationException("The " + targetType + " provider returned a host of another target.");
            if (actor.ModeratesCommentsOn(targetType)) host.ModeratorInScope = true;
            return host;
        }

        private async Task ChangedAsync(string commentId, CancellationToken token)
        {
            Comment? comment = await _Services.Database.Comments.ReadAsync(commentId, token).ConfigureAwait(false);
            if (comment == null) return;
            ContentTargetProvider? provider = Provider(comment.TargetType);
            if (provider != null) await provider.CommentChangedAsync(comment, token).ConfigureAwait(false);
        }

        private CommentHost FeedHost(FeedPost post, ContentActor actor)
        {
            return new CommentHost { TargetType = TargetTypeEnum.FeedPost, TargetId = post.Id, AuthorUserId = post.AuthorUserId, CommentCount = post.CommentCount, ModeratorInScope = actor.CanModerateFeed };
        }

        private static void RequireOwnConversation(FeedPost post)
        {
            if (post.Kind != FeedPostKindEnum.Original && post.SourceId != null)
                throw new ApiErrorException(409, "UseCommentTarget", "This card's conversation is " + (post.SourceType ?? "Thread") + " " + post.SourceId + "; use its commentTarget.");
        }

        private async Task<CommentView> CreateCoreAsync(ContentActor actor, string userId, CommentHost host, FeedPost? post, CommentRequest request, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            if (post != null)
            {
                if (post.StateAt(now) != FeedPostStateEnum.Published) throw ApiErrorException.NotFound("Feed post");
                if (!post.CommentsEnabled) throw new ApiErrorException(409, "CommentsDisabled", "Comments are turned off for this post.");
            }
            actor.RequireCanPost();
            EnforceCommentLimits(actor, now);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string body = FeedService.NormalizeBody(request.Body, _Services.Settings.Comments.MaxBodyLength, errors);
            List<string> mediaIds = (request.MediaIds ?? new List<string>()).ToList();
            if (body.Length == 0 && mediaIds.Count == 0) errors.TryAdd("body", "Write something or attach media.");
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);

            Comment? parent = null;
            if (!String.IsNullOrWhiteSpace(request.ParentCommentId))
            {
                parent = await _Services.Database.Comments.ReadAsync(request.ParentCommentId.Trim(), token).ConfigureAwait(false);
                if (parent == null || parent.TargetType != host.TargetType || !String.Equals(parent.TargetId, host.TargetId, StringComparison.Ordinal))
                    throw ApiErrorException.NotFound("Parent comment");
                if (parent.DeletedUtc != null || parent.ModerationState == ModerationStateEnum.Removed || !ContentAccessService.CanSeeCommentContent(parent, actor))
                    throw new ApiErrorException(409, "ParentUnavailable", "The comment you replied to is no longer available.");
            }
            string blockedBy = parent?.AuthorUserId ?? host.AuthorUserId;
            if (!String.IsNullOrEmpty(blockedBy) && !String.Equals(blockedBy, userId, StringComparison.Ordinal) && await _Services.Blocks.HasBlockedAsync(blockedBy, userId, token).ConfigureAwait(false))
                throw new ApiErrorException(403, "InteractionBlocked", "You cannot reply to this member.");
            if (mediaIds.Count > 0 && !host.AllowMedia && !actor.IsSystemAdmin) throw new ApiErrorException(403, "MediaNotAllowed", "Media is turned off here.");
            List<MediaAttachment> media = await _Services.Media.ValidateAttachableAsync(actor, mediaIds, _Services.Settings.Comments.MaxAttachments, 1, null, null, token).ConfigureAwait(false);
            if (!host.AllowVideo && !actor.IsSystemAdmin && media.Any(m => m.Kind == MediaKindEnum.Video)) throw new ApiErrorException(403, "VideoUploadNotAllowed", "Video is turned off here.");
            WordFilterResult filter = await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.Comment, FeedService.FilterFields(null, body, media), token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.Comment), token, TargetTypeEnum.Comment).ConfigureAwait(false);

            string id = IdGenerator.Generate(Constants.CommentIdPrefix);
            Comment comment = new Comment
            {
                Id = id,
                TargetType = host.TargetType,
                TargetId = host.TargetId,
                AuthorUserId = userId,
                Body = body,
                Excerpt = MarkdownNormalizer.Excerpt(body),
                ShowLinkPreview = request.ShowLinkPreview ?? true,
                AttachmentCount = media.Count,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            Place(comment, parent);
            if (media.Count > 0) await _Services.Media.AttachAsync(TargetTypeEnum.Comment, id, media.Select(m => m.Id).ToList(), token).ConfigureAwait(false);
            comment.LinkPreviewId = await _Services.LinkPreviews.ResolveForBodyAsync(body, comment.ShowLinkPreview, token).ConfigureAwait(false);
            comment = await _Services.Database.Comments.CreateAsync(comment, token).ConfigureAwait(false);
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Comment, id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.WordFilter.RecordFlagsAsync(filter, TargetTypeEnum.Comment, id, userId, token).ConfigureAwait(false);
            await _Services.Mentions.StoreAsync(body, TargetTypeEnum.Comment, id, userId, token).ConfigureAwait(false);

            List<NotificationDraft> drafts = new List<NotificationDraft>();
            if (post == null && host.TargetType == TargetTypeEnum.Idea)
            {
                // Idea conversations: the ideas module sends the comment notifications (Phase 5 section 9.1).
                if (IdeaAfterCreate != null) await IdeaAfterCreate(host, comment, parent, token).ConfigureAwait(false);
                return await ViewAsync(comment, actor, token).ConfigureAwait(false);
            }
            ContentTargetProvider? targetProvider = post == null ? Provider(host.TargetType) : null;
            if (targetProvider != null)
            {
                string path = targetProvider.PathOf(host.TargetType, host.TargetId) + "?comment=" + id;
                List<NotificationDraft> mentioned = await _Services.Mentions.ClaimDraftsAsync(TargetTypeEnum.Comment, id, userId, null, comment.Excerpt, token).ConfigureAwait(false);
                foreach (NotificationDraft draft in mentioned) draft.Path = path;
                if (mentioned.Count > 0) await _Services.Notifications.CreateForSourceAsync(mentioned, token).ConfigureAwait(false);
                await targetProvider.AfterCommentAsync(host, comment, parent, token).ConfigureAwait(false);
                return await ViewAsync(comment, actor, token).ConfigureAwait(false);
            }
            if (post == null)
            {
                // Thread conversations: the forum module sends the comment notifications (Phase 3 section 9.3).
                if (AfterCreate != null) await AfterCreate(host, comment, parent, token).ConfigureAwait(false);
                return await ViewAsync(comment, actor, token).ConfigureAwait(false);
            }
            if (parent == null)
            {
                drafts.Add(new NotificationDraft
                {
                    RecipientUserId = post.AuthorUserId,
                    Type = NotificationTypeEnum.PostComment,
                    ActorUserId = userId,
                    TargetType = TargetTypeEnum.Comment,
                    TargetId = id,
                    FeedPostId = post.Id,
                    CommentId = id,
                    Body = comment.Excerpt
                });
            }
            else
            {
                drafts.Add(new NotificationDraft
                {
                    RecipientUserId = parent.AuthorUserId,
                    Type = NotificationTypeEnum.CommentReply,
                    ActorUserId = userId,
                    TargetType = TargetTypeEnum.Comment,
                    TargetId = id,
                    FeedPostId = post.Id,
                    CommentId = id,
                    ParentCommentId = parent.Id,
                    Body = comment.Excerpt
                });
            }
            drafts.AddRange(await _Services.Mentions.ClaimDraftsAsync(TargetTypeEnum.Comment, id, userId, post.Id, comment.Excerpt, token).ConfigureAwait(false));
            await _Services.Notifications.CreateForSourceAsync(drafts, token).ConfigureAwait(false);
            if (AfterCreate != null && post.DiscussionThreadId != null) await AfterCreate(host, comment, parent, token).ConfigureAwait(false);
            return await ViewAsync(comment, actor, token).ConfigureAwait(false);
        }

        private async Task<CommentView> UpdateCoreAsync(ContentActor actor, string commentId, CommentRequest request, HashSet<string> present, CancellationToken token)
        {
            Comment comment = await _Services.ContentAccess.RequireCommentAsync(actor, commentId, token).ConfigureAwait(false);
            if (comment.DeletedUtc != null) throw ApiErrorException.NotFound("Comment");
            bool isAuthor = actor.UserId != null && String.Equals(comment.AuthorUserId, actor.UserId, StringComparison.Ordinal);
            if (!isAuthor)
            {
                if (ContentAccessService.CanSeeCommentContent(comment, actor)) throw ApiErrorException.Forbidden("Only the author can edit this comment.");
                throw ApiErrorException.NotFound("Comment");
            }
            actor.RequireCanPost();
            _Services.Limits.EnforceMember(actor, FeedService.EditsBucket, _Services.Settings.RateLimits.EditsPerHour, RateLimitService.Hour);
            DateTime now = DateTime.UtcNow;
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string body = present.Contains("body") ? FeedService.NormalizeBody(request.Body, _Services.Settings.Comments.MaxBodyLength, errors) : (comment.Body ?? String.Empty);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            List<MediaAttachment> before = await _Services.Database.Media.ListByTargetsAsync(TargetTypeEnum.Comment, new[] { comment.Id }, token).ConfigureAwait(false);
            List<MediaAttachment> after = before;
            if (present.Contains("mediaIds"))
            {
                after = await _Services.Media.ValidateAttachableAsync(actor, (request.MediaIds ?? new List<string>()).ToList(), _Services.Settings.Comments.MaxAttachments, 1,
                    TargetTypeEnum.Comment, comment.Id, token).ConfigureAwait(false);
            }
            if (body.Length == 0 && after.Count == 0) throw ApiErrorException.Field("body", "Write something or attach media.");
            WordFilterResult filter = await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.Comment, FeedService.FilterFields(null, body, after), token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.Comment), token, TargetTypeEnum.Comment).ConfigureAwait(false);

            bool mediaChanged = AttachmentSummaryText.Differs(before, after);
            bool bodyChanged = !String.Equals(body, comment.Body ?? String.Empty, StringComparison.Ordinal);
            bool silent = (now - comment.CreatedUtc).TotalSeconds <= _Services.Settings.Content.EditGraceSeconds;
            CommentRevision? revision = (bodyChanged || mediaChanged) && !silent ? new CommentRevision
            {
                Id = IdGenerator.Generate(Constants.CommentRevisionIdPrefix),
                CommentId = comment.Id,
                Body = comment.Body,
                AttachmentSummary = AttachmentSummaryText.Describe(before, after),
                CreatedUtc = now
            } : null;
            if (mediaChanged) await _Services.Media.AttachAsync(TargetTypeEnum.Comment, comment.Id, after.Select(m => m.Id).ToList(), token).ConfigureAwait(false);
            bool showPreview = present.Contains("showLinkPreview") ? (request.ShowLinkPreview ?? true) : comment.ShowLinkPreview;
            if (bodyChanged || showPreview != comment.ShowLinkPreview) comment.LinkPreviewId = await _Services.LinkPreviews.ResolveForBodyAsync(body, showPreview, token).ConfigureAwait(false);
            comment.Body = body;
            comment.Excerpt = MarkdownNormalizer.Excerpt(body);
            comment.ShowLinkPreview = showPreview;
            if (revision != null) comment.EditedUtc = now;
            comment.UpdatedUtc = now;
            comment = await _Services.Database.Comments.UpdateAsync(comment, revision, token).ConfigureAwait(false);
            if (revision != null) await _Services.Metadata.SnapshotAsync(TargetTypeEnum.Comment, comment.Id, TargetTypeEnum.CommentRevision, revision.Id, token).ConfigureAwait(false);
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Comment, comment.Id, present.Contains("labels") ? request.Labels : null, present.Contains("tags") ? request.Tags : null, token).ConfigureAwait(false);
            await _Services.WordFilter.RecordFlagsAsync(filter, TargetTypeEnum.Comment, comment.Id, comment.AuthorUserId, token).ConfigureAwait(false);
            if (bodyChanged)
            {
                await _Services.Mentions.StoreAsync(body, TargetTypeEnum.Comment, comment.Id, comment.AuthorUserId, token).ConfigureAwait(false);
                if (comment.TargetType == TargetTypeEnum.Thread && ThreadMentions != null)
                {
                    await ThreadMentions(comment, token).ConfigureAwait(false);
                    return await ViewAsync(comment, actor, token).ConfigureAwait(false);
                }
                List<NotificationDraft> drafts = await _Services.Mentions.ClaimDraftsAsync(TargetTypeEnum.Comment, comment.Id, comment.AuthorUserId,
                    comment.TargetType == TargetTypeEnum.FeedPost ? comment.TargetId : null, comment.Excerpt, token).ConfigureAwait(false);
                ContentTargetProvider? mentionProvider = Provider(comment.TargetType);
                if (mentionProvider != null)
                    foreach (NotificationDraft draft in drafts) draft.Path = mentionProvider.PathOf(comment.TargetType, comment.TargetId) + "?comment=" + comment.Id;
                if (drafts.Count > 0) await _Services.Notifications.CreateForSourceAsync(drafts, token).ConfigureAwait(false);
            }
            ContentTargetProvider? changed = Provider(comment.TargetType);
            if (changed != null) await changed.CommentChangedAsync(comment, token).ConfigureAwait(false);
            return await ViewAsync(comment, actor, token).ConfigureAwait(false);
        }

        private void Place(Comment comment, Comment? parent)
        {
            if (parent == null)
            {
                comment.ParentCommentId = null;
                comment.RootCommentId = comment.Id;
                comment.Depth = 0;
                comment.Path = comment.Id + "/";
                return;
            }
            List<string> segments = parent.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
            int maxDepth = Math.Max(1, _Services.Settings.Comments.MaxDepth);
            comment.RootCommentId = segments[0];
            if (parent.Depth >= maxDepth && segments.Count >= maxDepth)
            {
                List<string> ancestors = segments.Take(maxDepth).ToList();
                comment.ParentCommentId = ancestors[ancestors.Count - 1];
                comment.Depth = maxDepth;
                comment.ReplyToCommentId = parent.Id;
                comment.Path = String.Join("/", ancestors) + "/" + comment.Id + "/";
                return;
            }
            comment.ParentCommentId = parent.Id;
            comment.Depth = parent.Depth + 1;
            comment.Path = parent.Path + comment.Id + "/";
        }

        private void EnforceCommentLimits(ContentActor actor, DateTime now)
        {
            bool newAccount = actor.User != null && (now - actor.User.CreatedUtc).TotalHours < _Services.Settings.Comments.NewAccountHours;
            int perMinute = newAccount ? _Services.Settings.Comments.NewAccountPerMinute : _Services.Settings.Comments.PerMinute;
            int perDay = newAccount ? _Services.Settings.Comments.NewAccountPerDay : _Services.Settings.Comments.PerDay;
            _Services.Limits.CheckMember(actor, "comments.minute", perMinute, RateLimitService.Minute);
            _Services.Limits.CheckMember(actor, "comments.day", perDay, RateLimitService.Day);
            _Services.Limits.EnforceMember(actor, "comments.minute", perMinute, RateLimitService.Minute);
            _Services.Limits.EnforceMember(actor, "comments.day", perDay, RateLimitService.Day);
        }

        private async Task<List<CommentView>> BuildTreeAsync(List<Comment> roots, int depth, int childLimit, string ordering, ContentActor actor, CommentViewerFilter viewer, CancellationToken token)
        {
            Dictionary<string, List<Comment>> childrenOf = new Dictionary<string, List<Comment>>(StringComparer.Ordinal);
            Dictionary<string, int> totals = new Dictionary<string, int>(StringComparer.Ordinal);
            List<Comment> all = new List<Comment>(roots);
            List<Comment> current = roots;
            int limit = _TreeNodeLimit;
            for (int level = 1; level < depth && current.Count > 0; level++)
            {
                int budget = limit - all.Count;
                List<Comment> expand = current.Take(Math.Max(0, budget)).ToList();
                foreach (Comment skipped in current.Skip(expand.Count))
                {
                    totals[skipped.Id] = skipped.ChildCount;
                    childrenOf[skipped.Id] = new List<Comment>();
                }
                if (expand.Count == 0) break;
                CommentLevel next = await _Services.Database.Comments.ListChildrenAsync(expand.Select(c => c.Id).ToList(), ordering, childLimit, viewer, token).ConfigureAwait(false);
                foreach (KeyValuePair<string, int> pair in next.Totals) totals[pair.Key] = pair.Value;
                Dictionary<string, List<Comment>> fetched = next.Comments.GroupBy(c => c.ParentCommentId!).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
                List<Comment> taken = new List<Comment>();
                foreach (Comment parent in expand)
                {
                    List<Comment> kids = fetched.TryGetValue(parent.Id, out List<Comment>? group)
                        ? group.Take(Math.Min(childLimit, budget - taken.Count)).ToList() : new List<Comment>();
                    if (kids.Count > 0 || fetched.ContainsKey(parent.Id)) childrenOf[parent.Id] = kids;
                    taken.AddRange(kids);
                }
                all.AddRange(taken);
                current = taken;
            }
            Dictionary<string, CommentView> views = await _Services.ContentViews.CommentsAsync(all, actor, false, token).ConfigureAwait(false);
            return roots.Select(r => Assemble(r, 1, depth, ordering, childrenOf, totals, views)).ToList();
        }

        private CommentView Assemble(Comment comment, int level, int depth, string ordering, Dictionary<string, List<Comment>> childrenOf, Dictionary<string, int> totals, Dictionary<string, CommentView> views)
        {
            CommentView view = views[comment.Id];
            if (level < depth)
            {
                List<Comment> kids = childrenOf.TryGetValue(comment.Id, out List<Comment>? list) ? list : new List<Comment>();
                view.Children = kids.Select(k => Assemble(k, level + 1, depth, ordering, childrenOf, totals, views)).ToList();
                int remaining = (totals.TryGetValue(comment.Id, out int total) ? total : 0) - kids.Count;
                if (remaining > 0)
                {
                    string? cursor = kids.Count == 0 ? null : ChildrenToken(comment.Id, ordering, kids[kids.Count - 1]);
                    view.MoreChildren = new MoreChildrenView { RemainingCount = remaining, Cursor = cursor, ContinuationToken = cursor, Enumerate = ChildrenPath(comment.Id) };
                }
            }
            else
            {
                int remaining = Math.Max(comment.ChildCount, comment.DescendantCount > 0 ? 1 : 0);
                if (remaining > 0) view.MoreChildren = new MoreChildrenView { RemainingCount = remaining, Enumerate = ChildrenPath(comment.Id) };
            }
            return view;
        }

        private string ChildrenToken(string parentId, string ordering, Comment last)
        {
            EnumerationRequest request = _Services.Enumeration.Prepare(new EnumerationQuery { Ordering = ordering }, ContentCollections.CommentChildren, false, ChildrenScope(parentId, ordering));
            return ContinuationTokens.Encode(request.Fingerprint, ordering, CommentOrderingKeys.KeyOf(last, ordering), last.Id);
        }

        private static string ChildrenScope(string parentId, string ordering)
        {
            return "children|" + parentId + "|" + ordering;
        }

        private static string ChildrenPath(string commentId)
        {
            return Constants.ApiPrefix + "/comments/" + commentId + "/children/enumerate";
        }

        private static string CanonicalOrdering(string? ordering)
        {
            string name = String.IsNullOrWhiteSpace(ordering) ? ContentCollections.CommentChildren.DefaultOrdering : ordering.Trim();
            return ContentCollections.CommentChildren.Orderings.TryGetValue(name, out EnumerationOrdering? found) ? found.Name : name;
        }

        private static void ReadShape(EnumerationQuery query, out int depth, out int childLimit)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            long? d = query.FilterLong("depth", errors);
            long? c = query.FilterLong("childLimit", errors);
            depth = (int)(d ?? DefaultDepth);
            childLimit = (int)(c ?? DefaultChildLimit);
            if (d != null && (d.Value < 1 || d.Value > MaxDepthLevels)) errors["filters.depth"] = "Must be 1 to " + MaxDepthLevels + ".";
            if (c != null && (c.Value < 1 || c.Value > MaxChildLimit)) errors["filters.childLimit"] = "Must be 1 to " + MaxChildLimit + ".";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
        }

        private static EnumerationQuery WithoutShape(EnumerationQuery query, string? ordering)
        {
            Dictionary<string, JsonElement> filters = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, JsonElement> pair in query.Filters)
            {
                if (String.Equals(pair.Key, "depth", StringComparison.OrdinalIgnoreCase) || String.Equals(pair.Key, "childLimit", StringComparison.OrdinalIgnoreCase)) continue;
                filters[pair.Key] = pair.Value;
            }
            return new EnumerationQuery
            {
                MaxResults = query.MaxResults,
                ContinuationToken = query.ContinuationToken,
                Skip = query.Skip,
                Ordering = ordering ?? query.Ordering,
                Search = query.Search,
                CreatedAfterUtc = query.CreatedAfterUtc,
                CreatedBeforeUtc = query.CreatedBeforeUtc,
                Labels = new List<string>(query.Labels),
                LabelMatch = query.LabelMatch,
                Tags = new Dictionary<string, string>(query.Tags),
                Filters = filters
            };
        }

        private async Task<CommentViewerFilter> ViewerAsync(ContentActor actor, CancellationToken token)
        {
            CommentViewerFilter viewer = new CommentViewerFilter { ViewerUserId = actor.UserId, IsModerator = actor.CanModerateFeed };
            if (!String.IsNullOrEmpty(actor.UserId)) viewer.HiddenAuthorIds = (await _Services.Blocks.HiddenAuthorsForAsync(actor.UserId, token).ConfigureAwait(false)).ToList();
            return viewer;
        }

        private async Task<long> CommentCountAsync(Comment comment, CancellationToken token)
        {
            if (comment.TargetType == TargetTypeEnum.Thread) return (await _Services.Database.Threads.ReadAsync(comment.TargetId, token).ConfigureAwait(false))?.CommentCount ?? 0;
            if (comment.TargetType == TargetTypeEnum.Idea) return (await _Services.Database.Ideas.ReadAsync(comment.TargetId, token).ConfigureAwait(false))?.CommentCount ?? 0;
            ContentTargetProvider? provider = Provider(comment.TargetType);
            if (provider != null) return await provider.CommentCountAsync(comment.TargetType, comment.TargetId, token).ConfigureAwait(false);
            if (comment.TargetType != TargetTypeEnum.FeedPost) return 0;
            FeedPost? post = await _Services.Database.FeedPosts.ReadAsync(comment.TargetId, token).ConfigureAwait(false);
            return post?.CommentCount ?? 0;
        }

        private async Task<CommentView> ViewAsync(Comment comment, ContentActor actor, CancellationToken token)
        {
            Dictionary<string, CommentView> views = await _Services.ContentViews.CommentsAsync(new[] { comment }, actor, false, token).ConfigureAwait(false);
            return views[comment.Id];
        }

        private static CommentRevisionView ToView(CommentRevision revision)
        {
            return new CommentRevisionView
            {
                Id = revision.Id,
                Revision = revision.Revision,
                Body = revision.Body,
                AttachmentSummary = revision.AttachmentSummary,
                CreatedUtc = revision.CreatedUtc
            };
        }

        private static void Count(string action, string outcome, int depth)
        {
            TrellisKitTelemetry.Add(TrellisKitTelemetry.Comments, 1, "action", action, "outcome", outcome, "depth_bucket", DepthBucket(depth));
        }

        #endregion
    }
}
