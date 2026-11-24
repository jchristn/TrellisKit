namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Member ideas board routes (docs/PHASE5_CONTRACT.md section 7.1): the board, ideas, submissions, edits, votes,
    /// follows, the statuses list, and idea comments and reactions through the Phase 2 services.
    /// </summary>
    public class IdeaRoutes
    {
        #region Private-Members

        private const string _Tag = "Ideas";
        private const string _Idea = "Idea { id, title, body, excerpt, author: UserSummary | null, status { key, label, color, isClosed }, statusNote, statusUtc, voteCount, hasVoted, isFollowed, mergedInto { id, title } | null, commentTarget { type: Idea, id }, commentCount, reactionCounts, reactionTotal, myReactions, audience, locked, labels, tags, viewer { canVote, canComment, canReact, canReport, canEdit, canSetStatus, canMerge, canOpen, requires }, createdUtc, updatedUtc }";
        private const string _Summary = "IdeaSummary (Idea with body null)";
        private const string _Status = "IdeaStatus { id, key, label, color, isClosed, isInitial, sortOrder, isRetired, labels, tags }";
        private const string _CommentItem = "Comment (with children and moreChildren)";
        private const string _ReactionItem = "{ id, user: UserSummary, key, createdUtc, labels, tags }";
        private const string _Base = Constants.ApiPrefix + "/ideas";
        private const string _NotAvailable = "NotAvailable (features.ideas is off)";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public IdeaRoutes(PropertyDirectory directory)
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
            EnumerationRoutes.Add(post, _Base + "/statuses", IdeaCollections.Statuses, _Status,
                () => Doc("List idea statuses", _Tag, "The configured, active idea statuses in display order (for the board's status filter chips). Bounded by the status count (at most 50). Readable wherever the board is."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Ideas.Statuses.EnumerateAsync(actor, query, false, ctx.Token)));
            EnumerationRoutes.Add(post, _Base, IdeaCollections.Ideas, _Summary,
                () => Doc("List ideas", _Tag, "The ideas board. Orderings Top (votes, the default), Trending (votes in the last ideas.trendingDays), CreatedDescending, CreatedAscending, UpdatedDescending, Relevance (with search: title matches first, then votes). " +
                    "Filters status (keys), open (true: not a closed status), votedByMe, authorUserId, audience, includeMerged (merged ideas are excluded by default). Search covers title and body. Members ideas appear as locked stubs (locked true, body empty, viewer.canOpen false) to signed-in non-members in PartiallyGated. " +
                    "Signed-out callers see Public ideas when anonymous browsing is on. 404 NotAvailable when features.ideas is off."),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Ideas.Ideas.ListAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.POST, _Base, CreateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Submit an idea", _Tag, "Body { title (3 to 140), body (Markdown subset, at most 10000), audience?, labels?, tags? }. Only when ideas.memberSubmissions is Direct (staff may always submit). The author votes for and follows the idea. Word filter applies. Audience defaults to Members when ideas is gated, else the property default; Public needs ideas staff.")
                    .Body("IdeaRequest").Returns(_Idea, 201).Resp(400, "ValidationFailed or ContentFiltered").Resp(403, "Forbidden (submissions are staff only), PostingNotAllowed, or Muted").Resp(404, _NotAvailable).Resp(429, "RateLimited (ideas.ideasPerDay)"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read an idea", _Tag, "The idea; a merged idea returns its own payload with mergedInto. Counts are public; voters are never listed. 401 with required SignIn or 402 MembershipRequired for an idea the caller may only see as a locked stub.")
                    .PathParam("id", "Idea identifier").Returns(_Idea).Resp(401, "Unauthorized").Resp(402, "MembershipRequired").Resp(404, "NotFound or " + _NotAvailable));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}", UpdateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Edit an idea", _Tag, "Body { title?, body?, labels?, tags? } (omitted fields stay). The author while the idea is in the initial status; ideas staff any time (staff also set audience and showAuthor).")
                    .PathParam("id", "Idea identifier").Body("IdeaRequest").Returns(_Idea).Resp(400, "ValidationFailed or ContentFiltered").Resp(403, "Forbidden, PostingNotAllowed, or Muted").Resp(404, "NotFound").Resp(409, "IdeaNotEditable"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}/vote", ctx => VoteAsync(ctx, true), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Vote for an idea", _Tag, "Idempotent; one vote per member; voting follows the idea. Not on a closed status unless ideas.voteOnClosed.")
                    .PathParam("id", "Idea identifier").Returns(_Idea).Resp(401, "Unauthorized").Resp(402, "MembershipRequired").Resp(403, "Muted").Resp(404, "NotFound")
                    .Resp(409, "IdeaMerged (body names mergedInto) or IdeaClosed").Resp(429, "RateLimited (ideas.votesPerMinute)"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}/vote", ctx => VoteAsync(ctx, false), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Remove your vote", _Tag, "Idempotent.")
                    .PathParam("id", "Idea identifier").Returns(_Idea).Resp(404, "NotFound").Resp(409, "IdeaMerged").Resp(429, "RateLimited (ideas.votesPerMinute)"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}/follow", ctx => FollowAsync(ctx, true), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Follow an idea", _Tag, "Idempotent. Followers and voters get IdeaStatusChanged notifications.")
                    .PathParam("id", "Idea identifier").Returns("{ isFollowed }").Resp(402, "MembershipRequired").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}/follow", ctx => FollowAsync(ctx, false), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Unfollow an idea", _Tag, "Idempotent.")
                    .PathParam("id", "Idea identifier").Returns("{ isFollowed }").Resp(404, "NotFound"));
            RegisterConversation(post);
        }

        #endregion

        #region Private-Methods

        private void RegisterConversation(WatsonWebserver.Core.Routing.RoutingGroup post)
        {
            string comments = _Base + "/{id}/comments";
            string tree = "The idea's comment tree with the feed's rules (filters depth 1 to 5, childLimit 1 to 20; orderings Top, CreatedDescending, CreatedAscending). Comments of a merged idea stay readable. maxResults default 20, max 50.";
            post.Parameter.Add(HttpMethod.GET, comments, ctx => TreeAsync(ctx, false), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List an idea's comment tree", _Tag, tree)
                    .PathParam("id", "Idea identifier").Enumeration(_CommentItem, ContentCollections.CommentOrderings, "depth, childLimit").Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, comments + "/enumerate", ctx => TreeAsync(ctx, true), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate an idea's comment tree", _Tag, "EnumerationQuery body; same result as the GET form. " + tree)
                    .PathParam("id", "Idea identifier").Body("EnumerationQuery", false).Enumeration(_CommentItem, ContentCollections.CommentOrderings, "depth, childLimit", false)
                    .Resp(400, "ValidationFailed or InvalidContinuationToken").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, comments, CommentAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Comment on an idea", _Tag, "Body { body, parentCommentId?, mediaIds?, showLinkPreview?, labels?, tags? } as for feed comments. Notifies the idea's author (PostComment), the replied-to author (CommentReply), and mentioned members.")
                    .PathParam("id", "Idea identifier").Body("CommentRequest").Returns("Comment", 201).Resp(400, "ValidationFailed, MediaLimitExceeded, or ContentFiltered")
                    .Resp(402, "MembershipRequired").Resp(403, "PostingNotAllowed, MediaNotAllowed, Muted, or InteractionBlocked").Resp(404, "NotFound")
                    .Resp(409, "IdeaMerged (body names mergedInto), ParentUnavailable, or MediaNotAttachable").Resp(429, "RateLimited (comments per minute and day)"));
            string reactions = _Base + "/{id}/reactions";
            post.Parameter.Add(HttpMethod.PUT, reactions + "/{key}", ctx => ReactAsync(ctx, true), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("React to an idea", _Tag, "Adds the caller's reaction key (the property reaction set). Idempotent. Members who may take part and are not muted.")
                    .PathParam("id", "Idea identifier").PathParam("key", "Reaction key").Returns("{ reactionCounts, reactionTotal, myReactions }")
                    .Resp(400, "UnknownReaction").Resp(402, "MembershipRequired").Resp(403, "Muted or InteractionBlocked").Resp(404, "NotFound").Resp(429, "RateLimited (reactions.perMinute)"));
            post.Parameter.Add(HttpMethod.DELETE, reactions + "/{key}", ctx => ReactAsync(ctx, false), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Remove a reaction from an idea", _Tag, "Removes the caller's reaction key. Idempotent.")
                    .PathParam("id", "Idea identifier").PathParam("key", "Reaction key").Returns("{ reactionCounts, reactionTotal, myReactions }")
                    .Resp(400, "UnknownReaction").Resp(404, "NotFound").Resp(429, "RateLimited (reactions.perMinute)"));
            post.Parameter.Add(HttpMethod.GET, reactions, ctx => ReactionsAsync(ctx, false), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Who reacted to an idea", _Tag, "Newest first; filter key; members you blocked are omitted. maxResults default 50, max 100.")
                    .PathParam("id", "Idea identifier").Enumeration(_ReactionItem, "CreatedDescending, CreatedAscending", "key").Resp(400, "ValidationFailed or UnknownReaction").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, reactions + "/enumerate", ctx => ReactionsAsync(ctx, true), RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate who reacted to an idea", _Tag, "EnumerationQuery body (filters.key); same result as the GET form.")
                    .PathParam("id", "Idea identifier").Body("EnumerationQuery", false).Enumeration(_ReactionItem, "CreatedDescending, CreatedAscending", "key", false)
                    .Resp(400, "ValidationFailed or UnknownReaction").Resp(404, "NotFound"));
        }

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task<object> RunAsync<T>(HttpContextBase ctx, Func<ContentActor, Task<T>> body) where T : class
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            return await body(actor).ConfigureAwait(false);
        }

        private Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            return _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            IdeaRequest request = RouteHelper.ReadJson<IdeaRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Ideas.Ideas.CreateAsync(actor, Info(ctx), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ideas.Ideas.GetAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            IdeaRequest request = RouteHelper.ReadJson<IdeaRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ideas.Ideas.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), request, present, false, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task VoteAsync(HttpContextBase ctx, bool vote)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ideas.Ideas.VoteAsync(actor, RouteHelper.Param(ctx, "id"), vote, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task FollowAsync(HttpContextBase ctx, bool follow)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ideas.Ideas.FollowAsync(actor, RouteHelper.Param(ctx, "id"), follow, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task TreeAsync(HttpContextBase ctx, bool body)
        {
            EnumerationQuery query = body ? RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx) : RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx, "id");
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Comments.IdeaTreeAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CommentAsync(HttpContextBase ctx)
        {
            CommentRequest request = RouteHelper.ReadJson<CommentRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Comments.CreateOnIdeaAsync(actor, RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ReactAsync(HttpContextBase ctx, bool add)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            ApplicationServices services = _Directory.For(ctx);
            services.Ideas.Ideas.RequireEnabled();
            string id = RouteHelper.Param(ctx, "id");
            string key = RouteHelper.Param(ctx, "key");
            object state = add
                ? await services.Reactions.AddAsync(actor, TargetTypeEnum.Idea, id, key, ctx.Token).ConfigureAwait(false)
                : await services.Reactions.RemoveAsync(actor, TargetTypeEnum.Idea, id, key, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, state).ConfigureAwait(false);
        }

        private async Task ReactionsAsync(HttpContextBase ctx, bool body)
        {
            EnumerationQuery query = body ? RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx) : RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx, "id");
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            ApplicationServices services = _Directory.For(ctx);
            services.Ideas.Ideas.RequireEnabled();
            await RouteHelper.SendJsonAsync(ctx, 200, await services.Reactions.ListAsync(actor, TargetTypeEnum.Idea, RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
