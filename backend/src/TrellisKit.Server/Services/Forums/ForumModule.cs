namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The Phase 3 forum services of one property (docs/PHASE3_CONTRACT.md section 18) and their wiring into the
    /// Phase 2 services (comments, reactions, reports, moderation, media, notifications, feed cards, account lifecycle).
    /// </summary>
    public class ForumModule : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// Authorization component (audience, states, scope, mutes).
        /// </summary>
        public ForumAccessService Access { get; }

        /// <summary>
        /// Payload builders.
        /// </summary>
        public ForumViewBuilder Views { get; }

        /// <summary>
        /// Forums (member and admin routes).
        /// </summary>
        public ForumService Catalog { get; }

        /// <summary>
        /// Threads.
        /// </summary>
        public ThreadService Threads { get; }

        /// <summary>
        /// Polls.
        /// </summary>
        public PollService Polls { get; }

        /// <summary>
        /// Forum to feed portability.
        /// </summary>
        public PromotionService Promotions { get; }

        /// <summary>
        /// Forum notifications and fan-outs.
        /// </summary>
        public ForumNotificationService Notifications { get; }

        /// <summary>
        /// Forum moderation and mutes.
        /// </summary>
        public ForumModerationService Moderation { get; }

        /// <summary>
        /// Trending.
        /// </summary>
        public TrendingService Trending { get; }

        /// <summary>
        /// Search.
        /// </summary>
        public SearchService Search { get; }

        /// <summary>
        /// Following and Forums email digests.
        /// </summary>
        public ForumDigestService Digests { get; }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private int _Attached = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the module (call <see cref="Attach"/> once the Phase 2 services exist).
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public ForumModule(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            Access = new ForumAccessService(services);
            Views = new ForumViewBuilder(services);
            Catalog = new ForumService(services);
            Threads = new ThreadService(services);
            Polls = new PollService(services);
            Promotions = new PromotionService(services);
            Notifications = new ForumNotificationService(services);
            Moderation = new ForumModerationService(services);
            Trending = new TrendingService(services);
            Search = new SearchService(services);
            Digests = new ForumDigestService(services);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Wire the forum module into the Phase 2 services (idempotent).
        /// </summary>
        public void Attach()
        {
            if (Interlocked.Exchange(ref _Attached, 1) != 0) return;
            _Services.ContentViews.PostDecorator = Promotions.DecorateAsync;
            _Services.ContentViews.CommentDecorator = DecorateCommentsAsync;
            _Services.ContentAccess.ThreadVisible = ThreadVisibleAsync;
            _Services.ContentAccess.PostVisible = Promotions.CardVisibleAsync;
            if (_Services.Ai.Tools.Find(SearchForumsTool.Name) == null) _Services.Ai.Tools.Register(SearchForumsTool.Build(_Services));
            if (_Services.Ai.Tools.Find(GetThreadTool.Name) == null) _Services.Ai.Tools.Register(GetThreadTool.Build(_Services));
            if (_Services.Ai.Tools.Find(ListForumsTool.Name) == null) _Services.Ai.Tools.Register(ListForumsTool.Build(_Services));
            _Services.Comments.ThreadHostResolver = ThreadHostAsync;
            _Services.Comments.AfterCreate = AfterCommentAsync;
            _Services.Comments.ThreadMentions = ThreadCommentMentionsAsync;
            _Services.Reactions.ForumTargets = ReactionTargetAsync;
            _Services.Reports.ForumTargets = ReportTargetAsync;
            _Services.Moderation.Extension = Moderation;
            _Services.Notifications.ExtraFanout = Notifications.ProcessFanoutAsync;
            _Services.NotificationPreferences.AutoFollowReader = async (userId, token) =>
                await _Services.Database.Forums.ReadAutoFollowOnCommentAsync(userId, token).ConfigureAwait(false) ?? _Services.Settings.Forums.AutoFollowOnComment;
            _Services.NotificationPreferences.AutoFollowWriter = (userId, value, token) => _Services.Database.Forums.WriteAutoFollowOnCommentAsync(userId, value, DateTime.UtcNow, token);
            _Services.Media.AddSettledHandler(OnMediaSettledAsync);
            _Services.Media.AddDetachedHandler(OnMediaDetachedAsync);
            ForumExportSections.Register(_Services);
            Func<string, CancellationToken, Task>? previous = _Services.Accounts.AccountClosed;
            _Services.Accounts.AccountClosed = async (userId, token) =>
            {
                await EraseForAccountAsync(userId, token).ConfigureAwait(false);
                if (previous != null) await previous(userId, token).ConfigureAwait(false);
            };
        }

        /// <summary>
        /// Account deletion (section 13): threads soft-deleted, poll votes deleted with counts recomputed, follows,
        /// read state, preferences, and forum mutes deleted.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Threads deleted.</returns>
        public async Task<int> EraseForAccountAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            int deleted = 0;
            string? after = null;
            while (true)
            {
                List<ForumThread> threads = await _Services.Database.Threads.ListByAuthorAsync(userId, after, 100, false, token).ConfigureAwait(false);
                if (threads.Count == 0) break;
                foreach (ThreadContext context in await Access.ContextsAsync(threads, token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    if (await Threads.DeleteCoreAsync(context, userId, token).ConfigureAwait(false)) deleted++;
                    await _Services.Media.PurgeTargetAsync(TargetTypeEnum.Thread, context.Thread.Id, token).ConfigureAwait(false);
                }
                after = threads[threads.Count - 1].Id;
            }
            await _Services.Database.Polls.DeleteVotesForUserAsync(userId, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Database.Threads.DeleteFollowsForUserAsync(userId, token).ConfigureAwait(false);
            await _Services.Database.Forums.DeleteMutesForUserAsync(userId, token).ConfigureAwait(false);
            await Search.EnqueueAsync(TargetTypeEnum.User, userId, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await Search.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private async Task<bool> ThreadVisibleAsync(ContentActor actor, string threadId, CancellationToken token)
        {
            ThreadContext? context = await Access.LoadAsync(threadId, token).ConfigureAwait(false);
            return context != null && ForumAccessService.CanSeeThread(context, actor);
        }

        private async Task<CommentHost> ThreadHostAsync(ContentActor actor, string threadId, bool forWrite, CancellationToken token)
        {
            ThreadContext context = await Access.RequireThreadAsync(actor, threadId, token).ConfigureAwait(false);
            ForumThread thread = context.Thread;
            if (thread.Kind == ThreadKindEnum.LinkedFeedPost && thread.SourceId != null)
                throw new ApiErrorException(409, "UseCommentTarget", "This thread's conversation is FeedPost " + thread.SourceId + "; use its commentTarget.");
            if (forWrite)
            {
                if (ForumAccessService.StateOf(thread) != ThreadStateEnum.Published) throw ApiErrorException.NotFound("Thread");
                ForumAccessService.RequireOpen(context);
                await Access.RequireNotForumMutedAsync(actor, thread.ForumId, token).ConfigureAwait(false);
            }
            bool authorIsAdmin = await Access.IsSystemAdminAsync(thread.AuthorUserId, token).ConfigureAwait(false);
            return new CommentHost
            {
                TargetType = TargetTypeEnum.Thread,
                TargetId = thread.Id,
                AuthorUserId = thread.AuthorUserId,
                CommentCount = thread.CommentCount,
                AllowMedia = context.Forum.AllowMedia,
                AllowVideo = context.Forum.AllowMedia && context.Forum.AllowVideo,
                ModeratorInScope = ForumAccessService.CanModerate(actor, thread.ForumId, authorIsAdmin) || actor.CanModerateForum(thread.ForumId),
                AcceptedAnswerId = thread.Type == ThreadTypeEnum.Question && thread.HasAcceptedAnswer ? thread.AcceptedCommentId : null,
                Thread = thread
            };
        }

        private async Task AfterCommentAsync(CommentHost host, Comment comment, Comment? parent, CancellationToken token)
        {
            ForumThread? thread = host.Thread ?? await Notifications.ThreadOfCommentAsync(comment, token).ConfigureAwait(false);
            await Search.EnqueueAsync(TargetTypeEnum.Comment, comment.Id, token).ConfigureAwait(false);
            if (thread == null || thread.DeletedUtc != null) return;
            await Notifications.CommentCreatedAsync(thread, comment, parent, token).ConfigureAwait(false);
        }

        private async Task ThreadCommentMentionsAsync(Comment comment, CancellationToken token)
        {
            ForumThread? thread = await _Services.Database.Threads.ReadAsync(comment.TargetId, token).ConfigureAwait(false);
            if (thread != null) await Notifications.MentionsAsync(TargetTypeEnum.Comment, comment.Id, comment.AuthorUserId, thread, comment.Id, comment.Excerpt, token).ConfigureAwait(false);
            await Search.EnqueueAsync(TargetTypeEnum.Comment, comment.Id, token).ConfigureAwait(false);
        }

        private async Task<ReactionTarget?> ReactionTargetAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, bool forWrite, CancellationToken token)
        {
            if (targetType == TargetTypeEnum.Thread)
            {
                ThreadContext context = await Access.RequireThreadAsync(actor, targetId, token).ConfigureAwait(false);
                ForumThread thread = context.Thread;
                if (thread.Kind == ThreadKindEnum.LinkedFeedPost && thread.SourceId != null)
                    throw new ApiErrorException(409, "UseCommentTarget", "This thread's conversation is FeedPost " + thread.SourceId + "; use its commentTarget.");
                if (forWrite) await RequireReactableAsync(actor, context, token).ConfigureAwait(false);
                return new ReactionTarget { AuthorUserId = thread.AuthorUserId, Excerpt = thread.Title, ThreadId = thread.Id, ForumId = thread.ForumId };
            }
            Comment? comment = await _Services.Database.Comments.ReadAsync(targetId, token).ConfigureAwait(false);
            if (comment == null || comment.TargetType != TargetTypeEnum.Thread) return null;
            Comment visible = await _Services.ContentAccess.RequireCommentAsync(actor, targetId, token).ConfigureAwait(false);
            if (!ContentAccessService.CanSeeCommentContent(visible, actor)) throw ApiErrorException.NotFound("Comment");
            if (forWrite)
            {
                if (visible.ModerationState != ModerationStateEnum.None || visible.DeletedUtc != null) throw ApiErrorException.NotFound("Comment");
                ThreadContext context = await Access.RequireThreadAsync(actor, visible.TargetId, token).ConfigureAwait(false);
                await RequireReactableAsync(actor, context, token).ConfigureAwait(false);
            }
            return new ReactionTarget { AuthorUserId = visible.AuthorUserId, Excerpt = visible.Excerpt, ThreadId = visible.TargetId, ForumId = (await _Services.Database.Threads.ReadAsync(visible.TargetId, token).ConfigureAwait(false))?.ForumId };
        }

        private async Task RequireReactableAsync(ContentActor actor, ThreadContext context, CancellationToken token)
        {
            if (ForumAccessService.StateOf(context.Thread) != ThreadStateEnum.Published) throw ApiErrorException.NotFound("Thread");
            if (context.Thread.IsLocked) throw new ApiErrorException(409, "ThreadLocked", "This thread is locked.");
            await Access.RequireNotForumMutedAsync(actor, context.Thread.ForumId, token).ConfigureAwait(false);
        }

        private async Task<ModerationTarget?> ReportTargetAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, CancellationToken token)
        {
            string id = (targetId ?? String.Empty).Trim();
            if (targetType == TargetTypeEnum.Thread)
            {
                ThreadContext context = await Access.RequireThreadAsync(actor, id, token).ConfigureAwait(false);
                if (context.Thread.Kind == ThreadKindEnum.LinkedFeedPost && context.Source != null)
                    return new ModerationTarget { TargetType = TargetTypeEnum.FeedPost, TargetId = context.Source.Id, AuthorUserId = context.Source.AuthorUserId, Post = context.Source };
                return new ModerationTarget { TargetType = TargetTypeEnum.Thread, TargetId = context.Thread.Id, AuthorUserId = context.Thread.AuthorUserId, Thread = context.Thread, ForumId = context.Thread.ForumId };
            }
            if (targetType == TargetTypeEnum.FeedPost)
            {
                FeedPost? post = id.Length == 0 ? null : await _Services.Database.FeedPosts.ReadAsync(id, token).ConfigureAwait(false);
                if (post == null || post.Kind != FeedPostKindEnum.PromotedThread || post.SourceId == null) return null;
                ThreadContext context = await Access.RequireThreadAsync(actor, post.SourceId, token).ConfigureAwait(false);
                return new ModerationTarget { TargetType = TargetTypeEnum.Thread, TargetId = context.Thread.Id, AuthorUserId = context.Thread.AuthorUserId, Thread = context.Thread, ForumId = context.Thread.ForumId };
            }
            if (targetType == TargetTypeEnum.Comment)
            {
                Comment? comment = id.Length == 0 ? null : await _Services.Database.Comments.ReadAsync(id, token).ConfigureAwait(false);
                if (comment == null || comment.TargetType != TargetTypeEnum.Thread) return null;
                Comment visible = await _Services.ContentAccess.RequireCommentAsync(actor, id, token).ConfigureAwait(false);
                if (!ContentAccessService.CanSeeCommentContent(visible, actor)) throw ApiErrorException.NotFound("Comment");
                ForumThread? thread = await _Services.Database.Threads.ReadAsync(visible.TargetId, token).ConfigureAwait(false);
                return new ModerationTarget { TargetType = TargetTypeEnum.Comment, TargetId = visible.Id, AuthorUserId = visible.AuthorUserId, Comment = visible, ForumId = thread?.ForumId };
            }
            return null;
        }

        private async Task DecorateCommentsAsync(Dictionary<string, CommentView> views, IReadOnlyList<Comment> comments, ContentActor actor, CancellationToken token)
        {
            List<string> threadIds = comments.Where(c => c.TargetType == TargetTypeEnum.Thread).Select(c => c.TargetId).Distinct(StringComparer.Ordinal).ToList();
            if (threadIds.Count == 0) return;
            List<ThreadContext> contexts = await Access.ContextsAsync(await _Services.Database.Threads.ReadManyAsync(threadIds, token).ConfigureAwait(false), token).ConfigureAwait(false);
            Dictionary<string, ThreadContext> byId = contexts.ToDictionary(c => c.Thread.Id, StringComparer.Ordinal);
            HashSet<string> muted = await Access.MutedForumsAsync(actor, contexts.Select(c => c.Thread.ForumId), token).ConfigureAwait(false);
            foreach (Comment comment in comments)
            {
                if (comment.TargetType != TargetTypeEnum.Thread || !views.TryGetValue(comment.Id, out CommentView? view) || !byId.TryGetValue(comment.TargetId, out ThreadContext? context)) continue;
                bool frozen = context.Thread.IsLocked || muted.Contains(context.Thread.ForumId) || ForumAccessService.StateOf(context.Thread) != ThreadStateEnum.Published;
                if (frozen || context.Forum.IsArchived) view.Viewer.CanReply = false;
                if (frozen) view.Viewer.CanReact = false;
                if (actor.CanModerateForum(context.Thread.ForumId)) view.Viewer.CanModerate = view.Viewer.CanModerate || !(await Access.IsSystemAdminAsync(comment.AuthorUserId, token).ConfigureAwait(false));
                view.Viewer.CanAttachMedia = view.Viewer.CanAttachMedia && context.Forum.AllowMedia;
                view.Viewer.CanAttachVideo = view.Viewer.CanAttachVideo && context.Forum.AllowMedia && context.Forum.AllowVideo;
            }
        }

        private async Task OnMediaSettledAsync(MediaAttachment media, CancellationToken token)
        {
            if (media.AttachedTargetType != TargetTypeEnum.Thread || String.IsNullOrEmpty(media.AttachedTargetId)) return;
            ThreadContext? context = await Access.LoadAsync(media.AttachedTargetId, token).ConfigureAwait(false);
            if (context == null) return;
            if (context.Thread.PublishedUtc == null) await Threads.TryPublishAsync(context.Thread.Id, token).ConfigureAwait(false);
            else await Promotions.SyncPublicMediaAsync(context, token).ConfigureAwait(false);
        }

        private async Task OnMediaDetachedAsync(TargetTypeEnum targetType, string targetId, CancellationToken token)
        {
            if (targetType == TargetTypeEnum.Thread) await _Services.Database.Threads.RecomputeAttachmentsAsync(targetId, token).ConfigureAwait(false);
        }

        #endregion
    }
}
