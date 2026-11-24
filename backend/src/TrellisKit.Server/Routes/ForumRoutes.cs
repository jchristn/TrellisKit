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
    /// Member forum routes (Phase 3 contract sections 3 to 10): forums, threads, thread conversations, polls, question
    /// and feedback workflows, follows, forum to feed portability, and search. Members only.
    /// </summary>
    public class ForumRoutes
    {
        #region Private-Members

        private const string _ForumTag = "Forums";
        private const string _ThreadTag = "Threads";
        private const string _PollTag = "Polls";
        private const string _SearchTag = "Search";
        private const string _Forum = "Forum";
        private const string _Summary = "ThreadSummary";
        private const string _Thread = "Thread";
        private const string _Revision = "{ id, revision, title, body, type, attachmentSummary, editedBy: UserSummary, editedByRole: Author | Moderator, createdUtc, labels, tags }";
        private const string _Follow = "{ id, targetType: Forum | Thread, target: Forum | ThreadSummary, createdUtc, labels, tags }";
        private const string _Voter = "{ user: UserSummary, optionIds, createdUtc, labels, tags }";
        private const string _SearchItem = "SearchResult { type, id, title, snippet { text, highlights [{ start, length }] }, path, author: UserSummary | null, forum: { id, slug, name } | null, createdUtc, labels, tags }";
        private const string _CommentItem = "Comment (with children and moreChildren)";
        private const string _Forums = Constants.ApiPrefix + "/forums";
        private const string _Threads = Constants.ApiPrefix + "/threads";
        private const string _Polls = Constants.ApiPrefix + "/polls";
        private const string _NotAvailable = "NotAvailable (the property turns forums off: features.forums)";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public ForumRoutes(PropertyDirectory directory)
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
            RegisterForums(post);
            RegisterThreads(post);
            RegisterConversation(post);
            RegisterPolls(post);
            RegisterOther(post);
        }

        #endregion

        #region Private-Methods

        private void RegisterForums(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, _Forums, ForumCollections.Forums, _Forum,
                () => Doc("List forums", _ForumTag, "Forums the caller may read, in admin order (SortOrderAscending, the default; also NameAscending, ActivityDescending, and the Created orderings). Filters section, includeArchived (default false), followedOnly, audience. Search covers name and description. Each forum carries { threadCount, commentCount, lastActivityUtc, lastThread { id, title, author, lastActivityUtc }, isFollowed, unreadThreadCount (capped at 99; null signed out), viewer { canCreateThread, canFollow, canModerate, allowedThreadTypes } }. Members only."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Forums.Catalog.ListAsync(actor, query, ctx.Token)));
            post.Parameter.Add(HttpMethod.GET, _Forums + "/{idOrSlug}", GetForumAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a forum", _ForumTag, "By identifier or slug; 404 when the caller may not read it. Members only.")
                    .PathParam("idOrSlug", "Forum identifier or slug").Returns(_Forum).Resp(404, "NotFound or " + _NotAvailable));
            post.Parameter.Add(HttpMethod.PUT, _Forums + "/{id}/follow", FollowForumAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Follow a forum", _ForumTag, "Body { level: NewThreads (the default and the only level) }. Idempotent; forum follows are bounded by the forum count. Followers get NewThreadInForum notifications. Members only.")
                    .PathParam("id", "Forum identifier").Body("ForumFollowRequest", false).Returns(_Forum).Resp(400, "ValidationFailed").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.DELETE, _Forums + "/{id}/follow", UnfollowForumAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Unfollow a forum", _ForumTag, "Idempotent. Members only.")
                    .PathParam("id", "Forum identifier").Returns(_Forum).Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Forums + "/{id}/read", MarkForumReadAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Mark a forum read", _ForumTag, "Every thread in the forum counts as read up to now. Members only.")
                    .PathParam("id", "Forum identifier").Resp(204, "Marked").Resp(404, "NotFound"));
            EnumerationRoutes.Add(post, _Forums + "/{id}/threads", ForumCollections.ForumThreads, _Summary,
                () => Doc("List a forum's threads", _ThreadTag, "Published threads the caller may see. The first page of the default orderings starts with the forum's pinned threads (filters.includePinned false omits them); the continuation token covers unpinned threads only. Orderings Active (default: last activity), Hot (trending score), CreatedDescending, CreatedAscending, Top (reactions), Relevance (with search). Search covers title and body (filters.includeComments also matches comments) and adds snippet. Members only."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Forums.Threads.ListForumAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token)), "id");
            post.Parameter.Add(HttpMethod.POST, _Forums + "/{id}/threads", CreateThreadAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a thread", _ThreadTag, "Body { type: Discussion | Question | Poll | Feedback, title (for a Poll, the question), body, mediaIds, showLinkPreview, audience, poll { options [{ label }] (2 to polls.maxOptions), multipleChoice, maxChoices, allowVoteChange, resultsVisibility: Always | AfterVote | AfterClose, voterVisibility: Anonymous | Visible, closesUtc } (Poll threads), feedback { focus } (Feedback threads; at least one video), labels, tags }. " +
                    "Members with canPost who may create threads in the forum (threadCreation) and are not muted there. The thread publishes once its media has processed. The author follows it (source Author).")
                    .PathParam("id", "Forum identifier").Body("ThreadRequest").Returns(_Thread, 201).Resp(400, "ValidationFailed, MediaLimitExceeded, ContentFiltered, or FeedbackVideoRequired")
                    .Resp(403, "PostingNotAllowed, ThreadCreationRestricted, ThreadTypeNotAllowed, MediaNotAllowed, VideoUploadNotAllowed, Muted, or ForumMuted").Resp(404, "NotFound")
                    .Resp(409, "ForumArchived, MediaNotAttachable, or FeedbackLimitReached").Resp(429, "RateLimited (forums.threadsPerHour)"));
        }

        private void RegisterThreads(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            EnumerationRoutes.Add(post, _Threads, ForumCollections.Threads, _Summary,
                () => Doc("List threads across forums", _ThreadTag, "Published threads in every forum the caller may read. Filters forumIds, type, answered, unanswered, feedbackStatus, pollOpen, authorUserId, followedOnly, unreadOnly, hasMedia, hasVideo, audience, includeComments. Orderings as the forum list without the pinned prefix. Members only."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Forums.Threads.ListAsync(actor, query, ctx.Token)));
            post.Parameter.Add(HttpMethod.GET, _Threads + "/{id}", GetThreadAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a thread", _ThreadTag, "The thread with forum, poll, feedback, source (LinkedFeedPost threads), promotion, commentTarget, and viewer. Records the caller's read position. Authors see their threads in every state; moderators in scope see Hidden and Removed threads. Members only.")
                    .PathParam("id", "Thread identifier").Returns(_Thread).Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Threads + "/{id}", UpdateThreadAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit a thread", _ThreadTag, "Body { type, title, body, mediaIds, showLinkPreview, audience, poll, feedback { focus }, labels, tags } (omitted fields stay). The author may change every field (poll only before the first vote); a moderator in scope changes title, labels, tags, and type between Discussion and Question (audited as EditMetadata). Edits after content.editGraceSeconds store a revision.")
                    .PathParam("id", "Thread identifier").Body("ThreadRequest").Returns(_Thread).Resp(400, "ValidationFailed, MediaLimitExceeded, or ContentFiltered")
                    .Resp(403, "Forbidden, PostingNotAllowed, Muted, or ForumMuted").Resp(404, "NotFound").Resp(409, "ThreadLocked, PollHasVotes, or MediaNotAttachable").Resp(429, "RateLimited (rateLimits.editsPerHour)"));
            post.Parameter.Add(HttpMethod.DELETE, _Threads + "/{id}", DeleteThreadAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a thread", _ThreadTag, "The author, a moderator in scope, or SystemAdmin. Soft delete; ends any promotion.")
                    .PathParam("id", "Thread identifier").Resp(204, "Deleted").Resp(403, "Forbidden").Resp(404, "NotFound"));
            EnumerationRoutes.Add(post, _Threads + "/{id}/revisions", ForumCollections.ThreadRevisions, _Revision,
                () => Doc("List a thread's revisions", _ThreadTag, "Newest first; visible to everyone who can see the thread. Members only."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Forums.Threads.RevisionsAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token)), "id");
            AddThreadAction(post, "/pin", "Pin a thread", "Moderator in scope. Published threads only; at most forums.maxPinnedPerForum per forum; idempotent. Audited as Pin.", "PinLimitReached or NotPublished",
                (ctx, actor) => _Directory.For(ctx).Forums.Threads.SetPinnedAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), true, ctx.Token));
            AddThreadAction(post, "/unpin", "Unpin a thread", "Moderator in scope. Idempotent. Audited as Unpin.", null,
                (ctx, actor) => _Directory.For(ctx).Forums.Threads.SetPinnedAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), false, ctx.Token));
            post.Parameter.Add(HttpMethod.POST, _Threads + "/{id}/lock", LockAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Lock a thread", _ThreadTag, "Body { reason } (optional). Moderator in scope. Locked threads take no comments, reactions, votes, or author edits. Idempotent. Audited as Lock.")
                    .PathParam("id", "Thread identifier").Body("ThreadLockRequest", false).Returns(_Thread).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound"));
            AddThreadAction(post, "/unlock", "Unlock a thread", "Moderator in scope. Idempotent. Audited as Unlock.", null,
                (ctx, actor) => _Directory.For(ctx).Forums.Threads.SetLockedAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), false, null, ctx.Token));
            post.Parameter.Add(HttpMethod.POST, _Threads + "/{id}/move", MoveAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Move a thread", _ThreadTag, "Body { forumId, reason, notifyAuthor (default true) }. Moderator in scope of both forums. The thread's audience narrows to the target forum's. Audited as Move.")
                    .PathParam("id", "Thread identifier").Body("ThreadMoveRequest").Returns(_Thread).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "ForumArchived, ThreadTypeNotAllowed, or SameForum"));
            post.Parameter.Add(HttpMethod.POST, _Threads + "/{id}/accept-answer", AcceptAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Accept an answer", _ThreadTag, "Body { commentId }. The thread author or a moderator in scope; Question threads; any visible comment of the thread at any depth. Replaces any accepted answer; concurrent accepts serialize. AnswerAccepted notifies the comment author.")
                    .PathParam("id", "Thread identifier").Body("AcceptAnswerRequest").Returns(_Thread).Resp(400, "ValidationFailed or InvalidAcceptedAnswer").Resp(403, "Forbidden").Resp(404, "NotFound"));
            AddThreadAction(post, "/accept-answer", "Clear the accepted answer", "The thread author or a moderator in scope. Idempotent.", null,
                (ctx, actor) => _Directory.For(ctx).Forums.Threads.SetAcceptedAnswerAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), null, ctx.Token), HttpMethod.DELETE);
            post.Parameter.Add(HttpMethod.PUT, _Threads + "/{id}/feedback", FeedbackAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Set a feedback request's status", _ThreadTag, "Body { status: Open | InReview | Reviewed | Closed, reviewCommentId? (Reviewed) }. Moderators in scope for every transition; the author may set Closed. Reviewed notifies the author.")
                    .PathParam("id", "Thread identifier").Body("FeedbackStatusRequest").Returns(_Thread).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "NotAFeedbackRequest or InvalidTransition"));
            post.Parameter.Add(HttpMethod.PUT, _Threads + "/{id}/follow", ctx => FollowThreadAsync(ctx, true), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Follow a thread", _ThreadTag, "Idempotent. Beyond forums.maxFollowsPerMember the least recently active followed thread is unfollowed. Followers get ThreadReply notifications. Members only.")
                    .PathParam("id", "Thread identifier").Returns("{ isFollowed }").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.DELETE, _Threads + "/{id}/follow", ctx => FollowThreadAsync(ctx, false), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Unfollow a thread", _ThreadTag, "Idempotent; an unfollow sticks against later automatic follows. Members only.")
                    .PathParam("id", "Thread identifier").Returns("{ isFollowed }").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Threads + "/{id}/promote", PromoteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Promote a thread to the feed", _ThreadTag, "Body { title?, excerpt?, pin? (SystemAdmin only), audience? (may only narrow the thread's), notifyMembers? (default true: a NewFeedPost fan-out) }. Members with canPromoteToFeed (SystemAdmin by default) who can read the thread. Creates a PromotedThread feed card whose conversation stays on the thread; the author gets ThreadPromoted. One promotion per thread.")
                    .PathParam("id", "Thread identifier").Body("PromoteRequest", false).Returns("FeedPost", 201).Resp(400, "ValidationFailed").Resp(403, "PromotionNotAllowed").Resp(404, "NotFound").Resp(409, "AlreadyPromoted, NotPublished, or PinLimitReached"));
            AddThreadAction(post, "/unpromote", "End a thread's promotion", "SystemAdmin, the promoter, or a moderator in scope. Deletes the feed card. Idempotent.", null,
                (ctx, actor) => _Directory.For(ctx).Forums.Promotions.UnpromoteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token));
        }

        private void RegisterConversation(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            string comments = _Threads + "/{id}/comments";
            string tree = "The thread's comment tree with the feed's rules (filters depth 1 to 5, childLimit 1 to 20; orderings Top, CreatedDescending, CreatedAscending). The result adds acceptedAnswerId; comments carry isAcceptedAnswer and isFeedbackReview. " +
                "A LinkedFeedPost thread answers 409 UseCommentTarget: use its commentTarget (the feed post). maxResults default 20, max 50. Members only.";
            post.Parameter.Add(HttpMethod.GET, comments, TreeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List a thread's comment tree", _ThreadTag, tree)
                    .PathParam("id", "Thread identifier").Enumeration(_CommentItem, ContentCollections.CommentOrderings, "depth, childLimit").Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound").Resp(409, "UseCommentTarget"));
            post.Parameter.Add(HttpMethod.POST, comments + "/enumerate", EnumerateTreeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate a thread's comment tree", _ThreadTag, "EnumerationQuery body; same result as the GET form. " + tree)
                    .PathParam("id", "Thread identifier").Body("EnumerationQuery", false).Enumeration(_CommentItem, ContentCollections.CommentOrderings, "depth, childLimit", false)
                    .Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound").Resp(409, "UseCommentTarget"));
            post.Parameter.Add(HttpMethod.POST, comments, CreateCommentAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Comment on a thread", _ThreadTag, "Body { body, parentCommentId?, mediaIds?, showLinkPreview?, labels?, tags? } as for feed comments; media only when the forum allows it. The commenter follows the thread when autoFollowOnComment is on. Members with canPost who are not muted in the forum.")
                    .PathParam("id", "Thread identifier").Body("CommentRequest").Returns("Comment", 201).Resp(400, "ValidationFailed, MediaLimitExceeded, or ContentFiltered")
                    .Resp(403, "PostingNotAllowed, MediaNotAllowed, VideoUploadNotAllowed, Muted, ForumMuted, or InteractionBlocked").Resp(404, "NotFound")
                    .Resp(409, "ThreadLocked, ForumArchived, ParentUnavailable, MediaNotAttachable, or UseCommentTarget").Resp(429, "RateLimited (comments per minute and day)"));
        }

        private void RegisterPolls(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            post.Parameter.Add(HttpMethod.PUT, _Polls + "/{id}/votes", VoteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Vote in a poll", _PollTag, "Body { optionIds } (one, or up to maxChoices when multipleChoice). Replaces the caller's vote when allowVoteChange. Counts stay exact under concurrent votes. Members who are not muted in the forum.")
                    .PathParam("id", "Poll identifier").Body("PollVoteRequest").Returns("Poll").Resp(400, "ValidationFailed").Resp(403, "Muted or ForumMuted").Resp(404, "NotFound")
                    .Resp(409, "PollClosed, PollVoteLocked, ThreadLocked, or ForumArchived").Resp(429, "RateLimited (polls.votesPerMinute)"));
            post.Parameter.Add(HttpMethod.DELETE, _Polls + "/{id}/votes", UnvoteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Remove your poll vote", _PollTag, "Only when allowVoteChange. Idempotent. Members only.")
                    .PathParam("id", "Poll identifier").Returns("Poll").Resp(404, "NotFound").Resp(409, "PollClosed, PollVoteLocked, or ThreadLocked"));
            post.Parameter.Add(HttpMethod.POST, _Polls + "/{id}/close", ClosePollAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Close a poll", _PollTag, "The thread author or a moderator in scope. Idempotent; voters are notified (PollClosed).")
                    .PathParam("id", "Poll identifier").Returns("Poll").Resp(403, "Forbidden").Resp(404, "NotFound"));
            EnumerationRoutes.Add(post, _Polls + "/{id}/voters", ForumCollections.PollVoters, _Voter,
                () => Doc("List a poll's voters", _PollTag, "Only when voterVisibility is Visible (the thread author and moderators in scope always may). Filter optionId. Members only."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Forums.Polls.VotersAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token)), "id");
        }

        private void RegisterOther(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            post.Parameter.Add(HttpMethod.POST, Constants.ApiPrefix + "/feed/{id}/discuss", DiscussAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Discuss a feed post in a forum", _ThreadTag, "Body { forumId, title?, labels?, tags? }. Members with canPromoteToFeed (SystemAdmin by default). Creates a LinkedFeedPost thread whose conversation is the feed post's (commentTarget FeedPost). One linked thread per post.")
                    .PathParam("id", "Feed post identifier").Body("DiscussRequest").Returns(_Thread, 201).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "AlreadyDiscussed, ForumArchived, or NotPublished"));
            post.Parameter.Add(HttpMethod.POST, Constants.ApiPrefix + "/feed/{id}/undiscuss", UndiscussAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Remove a feed post's forum thread", _ThreadTag, "SystemAdmin or the member who linked it. Deletes the LinkedFeedPost thread; the feed post and its comments stay. Idempotent.")
                    .PathParam("id", "Feed post identifier").Returns("FeedPost").Resp(403, "Forbidden").Resp(404, "NotFound"));
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/me/follows", ForumCollections.Follows, _Follow,
                () => Doc("List your follows", _ThreadTag, "Your forum and thread follows, newest first. Filter targetType (Forum or Thread). Signed in."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Forums.Threads.MyFollowsAsync(actor, query, ctx.Token)));
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/search", ForumCollections.Search, _SearchItem,
                () => Doc("Search", _SearchTag, "One search across feed posts, threads, comments, and members the caller may see (filters.types narrows: FeedPost, Thread, Comment, User, and the Phase 4 types Venue, Instructor, Event, Series, Lesson while their feature is wired; those results carry path and pass the object visibility check). Lemmatized; stop words removed; results carry a highlighted snippet. Ordering Relevance (default) or the Created orderings. search is required (1 to 200 characters). The search feature: 404 NotAvailable when features.search is false; 402 MembershipRequired (feature search) for non-members when search is gated in PartiallyGated; an empty page when the deployment setting search.enabled is off. Members only."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Forums.Search.GlobalAsync(actor, query, ctx.Token)));
        }

        private void AddThreadAction<T>(WatsonWebserver.Core.Routing.RoutingGroup post, string suffix, string summary, string description, string? conflict,
            Func<HttpContextBase, ContentActor, Task<T>> action, HttpMethod? method = null) where T : class
        {
            WatsonWebserver.Core.OpenApi.OpenApiRouteMetadata doc = Doc(summary, _ThreadTag, description).PathParam("id", "Thread identifier").Returns(_Thread).Resp(403, "Forbidden").Resp(404, "NotFound");
            if (conflict != null) doc = doc.Resp(409, conflict);
            post.Parameter.Add(method ?? HttpMethod.POST, _Threads + "/{id}" + suffix, async ctx =>
            {
                ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, await action(ctx, actor).ConfigureAwait(false)).ConfigureAwait(false);
            }, RouteHelper.HandleExceptionAsync, openApiMetadata: doc);
        }

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

        private async Task GetForumAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Catalog.GetAsync(actor, RouteHelper.Param(ctx, "idOrSlug"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task FollowForumAsync(HttpContextBase ctx)
        {
            ForumFollowRequest request = RouteHelper.ReadJson<ForumFollowRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Catalog.FollowAsync(actor, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UnfollowForumAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Catalog.UnfollowAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task MarkForumReadAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await _Directory.For(ctx).Forums.Catalog.MarkReadAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task CreateThreadAsync(HttpContextBase ctx)
        {
            ThreadRequest request = RouteHelper.ReadJson<ThreadRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Forums.Threads.CreateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetThreadAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Threads.GetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateThreadAsync(HttpContextBase ctx)
        {
            ThreadRequest request = RouteHelper.ReadJson<ThreadRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Threads.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteThreadAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await _Directory.For(ctx).Forums.Threads.DeleteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task LockAsync(HttpContextBase ctx)
        {
            ThreadLockRequest request = RouteHelper.ReadJson<ThreadLockRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Threads.SetLockedAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), true, request.Reason, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task MoveAsync(HttpContextBase ctx)
        {
            ThreadMoveRequest request = RouteHelper.ReadJson<ThreadMoveRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Threads.MoveAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task AcceptAsync(HttpContextBase ctx)
        {
            AcceptAnswerRequest request = RouteHelper.ReadJson<AcceptAnswerRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(request.CommentId)) throw TrellisKit.Core.Exceptions.ApiErrorException.Validation(new Dictionary<string, string> { { "commentId", "Required." } });
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Threads.SetAcceptedAnswerAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request.CommentId, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task FeedbackAsync(HttpContextBase ctx)
        {
            FeedbackStatusRequest request = RouteHelper.ReadJson<FeedbackStatusRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Threads.SetFeedbackAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task FollowThreadAsync(HttpContextBase ctx, bool follow)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Threads.SetFollowAsync(actor, RouteHelper.Param(ctx, "id"), follow, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PromoteAsync(HttpContextBase ctx)
        {
            PromoteRequest request = RouteHelper.ReadJson<PromoteRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Forums.Promotions.PromoteAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

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
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Comments.ThreadTreeAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateCommentAsync(HttpContextBase ctx)
        {
            CommentRequest request = RouteHelper.ReadJson<CommentRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Comments.CreateOnThreadAsync(actor, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task VoteAsync(HttpContextBase ctx)
        {
            PollVoteRequest request = RouteHelper.ReadJson<PollVoteRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Polls.VoteAsync(actor, RouteHelper.Param(ctx, "id"), request.OptionIds ?? new List<string>(), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UnvoteAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Polls.VoteAsync(actor, RouteHelper.Param(ctx, "id"), null, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ClosePollAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Polls.CloseAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DiscussAsync(HttpContextBase ctx)
        {
            DiscussRequest request = RouteHelper.ReadJson<DiscussRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Forums.Promotions.DiscussAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UndiscussAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Forums.Promotions.UndiscussAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
