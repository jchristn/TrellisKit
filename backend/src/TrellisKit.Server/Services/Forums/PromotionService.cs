namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Forum to feed portability (docs/PHASE3_CONTRACT.md section 8): promote a thread to the feed, open a feed post for
    /// discussion in a forum, unpromote and unlink, the one-conversation coupling of cards and linked threads, and
    /// public media of sample threads.
    /// </summary>
    public class PromotionService
    {
        #region Public-Members

        /// <summary>
        /// Longest card excerpt override.
        /// </summary>
        public const int MaxExcerptLength = 300;

        /// <summary>
        /// Most attachments shown on a promoted card.
        /// </summary>
        public const int CardAttachmentMax = 4;

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public PromotionService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/threads/{id}/promote.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin or canPromoteToFeed).</param>
        /// <param name="info">Request information.</param>
        /// <param name="threadId">Thread.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The feed card.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 PromotionNotAllowed, 404 NotFound, 409 AlreadyPromoted or PinLimitReached, 429 RateLimited.</exception>
        public async Task<FeedPostView> PromoteAsync(ContentActor actor, RequestInfo info, string threadId, PromoteRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            actor.RequireUserId();
            RequirePromoter(actor);
            ThreadContext context = await _Services.Forums.Access.RequireThreadAsync(actor, threadId, token).ConfigureAwait(false);
            ForumThread thread = context.Thread;
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (ForumAccessService.StateOf(thread) != ThreadStateEnum.Published) errors["threadId"] = "Only published threads can be promoted.";
            if (thread.Kind != ThreadKindEnum.Original) errors["threadId"] = "A discussion thread of a feed post cannot be promoted.";
            string? title = Override(request.Title, ThreadService.MaxTitleLength, "title", errors);
            string? excerpt = Override(request.Excerpt, MaxExcerptLength, "excerpt", errors);
            ContentAudienceEnum audience = request.Audience ?? thread.Audience;
            if (ForumAccessService.Rank(audience) < ForumAccessService.Rank(thread.Audience)) errors["audience"] = "The card cannot be broader than the thread (" + thread.Audience + ").";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (thread.PromotedFeedPostId != null) throw new ApiErrorException(409, "AlreadyPromoted", "This thread already has a feed card.");
            bool pin = request.Pin == true && actor.IsSystemAdmin;
            if (pin && await _Services.Database.FeedPosts.CountPinnedAsync(token).ConfigureAwait(false) >= _Services.Settings.Feed.MaxPinned)
                throw new ApiErrorException(409, "PinLimitReached", "At most " + _Services.Settings.Feed.MaxPinned + " posts can be pinned. Unpin one first.");
            _Services.Limits.EnforceMember(actor, "forums.promotions", _Services.Settings.Forums.PromotionsPerHour, RateLimitService.Hour);
            DateTime now = DateTime.UtcNow;
            string text = excerpt ?? thread.Excerpt;
            FeedPost card = await _Services.Database.FeedPosts.CreateAsync(new FeedPost
            {
                Id = IdGenerator.Generate(Constants.FeedPostIdPrefix),
                AuthorUserId = thread.AuthorUserId,
                Kind = FeedPostKindEnum.PromotedThread,
                SourceType = TargetTypeEnum.Thread.ToString(),
                SourceId = thread.Id,
                Title = title ?? thread.Title,
                Body = text,
                Excerpt = text,
                Visibility = audience == ContentAudienceEnum.Public ? FeedVisibilityEnum.Public : FeedVisibilityEnum.Members,
                Audience = audience,
                ShowLinkPreview = false,
                CommentsEnabled = true,
                PublishedUtc = now,
                IsPinned = pin,
                PinnedUtc = pin ? now : null,
                PinnedByUserId = pin ? actor.UserId : null,
                AttachmentCount = thread.AttachmentCount,
                HasVideo = thread.HasVideo,
                CreatedUtc = now,
                UpdatedUtc = now
            }, token).ConfigureAwait(false);
            if (!await _Services.Database.Threads.SetPromotionAsync(thread.Id, card.Id, actor.UserId, now, token).ConfigureAwait(false))
            {
                await _Services.Database.FeedPosts.SoftDeleteAsync(card.Id, actor.UserId, now, token).ConfigureAwait(false);
                throw new ApiErrorException(409, "AlreadyPromoted", "This thread already has a feed card.");
            }
            if (request.NotifyMembers ?? true) await _Services.Notifications.StartFanoutAsync(NotificationTypeEnum.NewFeedPost, card.Id, token).ConfigureAwait(false);
            await _Services.Forums.Notifications.ThreadPromotedAsync(actor, thread, card.Id, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "thread.promote", TargetTypeEnum.Thread.ToString(), thread.Id, card.Id, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.FeedPost, card.Id, token).ConfigureAwait(false);
            ForumTelemetry.Count(ForumTelemetry.Promotions, "action", "promote");
            FeedPost stored = (await _Services.Database.FeedPosts.ReadAsync(card.Id, token).ConfigureAwait(false))!;
            return (await _Services.ContentViews.FeedPostsAsync(new[] { stored }, actor, false, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// POST /api/v1/threads/{id}/unpromote: SystemAdmin, the promoter, or a moderator in scope.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="threadId">Thread.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Thread.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound, 429 RateLimited.</exception>
        public async Task<ThreadView> UnpromoteAsync(ContentActor actor, RequestInfo info, string threadId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ThreadContext context = await _Services.Forums.Access.RequireThreadAsync(actor, threadId, token).ConfigureAwait(false);
            ForumThread thread = context.Thread;
            bool promoter = actor.UserId != null && String.Equals(thread.PromotedByUserId, actor.UserId, StringComparison.Ordinal);
            if (!actor.IsSystemAdmin && !promoter && !actor.CanModerateForum(thread.ForumId)) throw ApiErrorException.Forbidden("Only a SystemAdmin, the promoter, or a moderator in scope can unpromote this thread.");
            _Services.Limits.EnforceMember(actor, "forums.promotions", _Services.Settings.Forums.PromotionsPerHour, RateLimitService.Hour);
            if (await EndPromotionAsync(thread, token).ConfigureAwait(false))
            {
                await _Services.Audit.WriteAsync(info, "thread.unpromote", TargetTypeEnum.Thread.ToString(), thread.Id, Summary(thread), token).ConfigureAwait(false);
                ForumTelemetry.Count(ForumTelemetry.Promotions, "action", "unpromote");
            }
            return await _Services.Forums.Threads.ReloadAsync(thread.Id, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// End a thread's active feed card (soft-delete the card, keep the thread and its conversation).
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a promotion ended.</returns>
        public async Task<bool> EndPromotionAsync(ForumThread thread, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(thread);
            if (thread.PromotedFeedPostId == null) return false;
            FeedPost? card = await _Services.Database.FeedPosts.ReadAsync(thread.PromotedFeedPostId, token).ConfigureAwait(false);
            if (card != null) await _Services.Feed.DeleteCoreAsync(card, null, token).ConfigureAwait(false);
            await _Services.Database.Threads.SetPromotionAsync(thread.Id, null, null, DateTime.UtcNow, token).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// POST /api/v1/feed/{id}/discuss: open a feed post for discussion in a forum (one thread per post).
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin or canPromoteToFeed).</param>
        /// <param name="info">Request information.</param>
        /// <param name="feedPostId">Feed post.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The discussion thread.</returns>
        /// <exception cref="ApiErrorException">400 ThreadTypeNotAllowed or ValidationFailed, 403 Forbidden, 404 NotFound, 409 AlreadyPromoted or ForumArchived.</exception>
        public async Task<ThreadView> DiscussAsync(ContentActor actor, RequestInfo info, string feedPostId, DiscussRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            actor.RequireUserId();
            _Services.Forums.Access.RequireEnabled();
            if (!actor.IsSystemAdmin && !actor.CanPromoteToFeed) throw ApiErrorException.Forbidden("Opening a feed post for discussion needs SystemAdmin or canPromoteToFeed.");
            FeedPost post = await _Services.ContentAccess.RequireVisiblePostAsync(actor, feedPostId, token).ConfigureAwait(false);
            if (post.StateAt(DateTime.UtcNow) != FeedPostStateEnum.Published || post.Kind != FeedPostKindEnum.Original) throw ApiErrorException.NotFound("Feed post");
            if (String.IsNullOrWhiteSpace(request.ForumId)) throw ApiErrorException.Field("forumId", "Required.");
            Forum forum = await _Services.Forums.Access.RequireForumAsync(actor, request.ForumId, token).ConfigureAwait(false);
            if (forum.IsArchived) throw new ApiErrorException(409, "ForumArchived", "This forum is archived and read-only.");
            if (!forum.AllowedThreadTypes.Contains(ThreadTypeEnum.Discussion)) throw new ApiErrorException(400, "ThreadTypeNotAllowed", "This forum does not allow Discussion threads.");
            if (post.DiscussionThreadId != null) throw new ApiErrorException(409, "AlreadyPromoted", "This post already has a discussion thread.");
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string? title = Override(request.Title, ThreadService.MaxTitleLength, "title", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.Thread), token, TargetTypeEnum.Thread).ConfigureAwait(false);
            _Services.Limits.EnforceMember(actor, "forums.promotions", _Services.Settings.Forums.PromotionsPerHour, RateLimitService.Hour);
            string fallback = String.IsNullOrWhiteSpace(post.Title) ? Cut(post.Excerpt, ThreadService.MaxTitleLength) : post.Title!;
            string threadTitle = title ?? (MarkdownNormalizer.Length(fallback) >= ThreadService.MinTitleLength ? fallback : "Discussion");
            DateTime now = DateTime.UtcNow;
            string id = IdGenerator.Generate(ForumIdPrefixes.Thread);
            ForumThread thread = new ForumThread
            {
                Id = id,
                ForumId = forum.Id,
                Kind = ThreadKindEnum.LinkedFeedPost,
                Type = ThreadTypeEnum.Discussion,
                AuthorUserId = post.AuthorUserId,
                Title = threadTitle,
                Body = null,
                Excerpt = post.Excerpt,
                ShowLinkPreview = false,
                Audience = ForumAccessService.Narrowest(post.Audience, forum.Audience),
                SourceType = TargetTypeEnum.FeedPost.ToString(),
                SourceId = post.Id,
                CommentTargetType = TargetTypeEnum.FeedPost,
                CommentTargetId = post.Id,
                LastActivityUtc = now,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            try
            {
                await _Services.Database.Threads.CreateAsync(thread, null, new ThreadFollow { Id = IdGenerator.Generate(ForumIdPrefixes.ThreadFollow), ThreadId = id, UserId = post.AuthorUserId, Source = FollowSourceEnum.Author, CreatedUtc = now }, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "AlreadyPromoted", "This post already has a discussion thread.");
            }
            if (!await _Services.Database.FeedPosts.SetDiscussionAsync(post.Id, id, actor.UserId, now, token).ConfigureAwait(false))
            {
                await _Services.Database.Threads.SoftDeleteAsync(id, actor.UserId, now, token).ConfigureAwait(false);
                throw new ApiErrorException(409, "AlreadyPromoted", "This post already has a discussion thread.");
            }
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Thread, id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.Forums.Threads.TryPublishAsync(id, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "feedPost.discuss", TargetTypeEnum.FeedPost.ToString(), post.Id, id, token).ConfigureAwait(false);
            ForumTelemetry.Count(ForumTelemetry.Promotions, "action", "discuss");
            return await _Services.Forums.Threads.ReloadAsync(id, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/feed/{id}/undiscuss: delete the linked thread (SystemAdmin or the member who linked it).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="feedPostId">Feed post.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The feed post.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<FeedPostView> UndiscussAsync(ContentActor actor, RequestInfo info, string feedPostId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            _Services.Forums.Access.RequireEnabled();
            FeedPost post = await _Services.ContentAccess.RequireVisiblePostAsync(actor, feedPostId, token).ConfigureAwait(false);
            bool linker = actor.UserId != null && String.Equals(post.DiscussionLinkedByUserId, actor.UserId, StringComparison.Ordinal);
            if (!actor.IsSystemAdmin && !linker) throw ApiErrorException.Forbidden("Only a SystemAdmin or the member who opened the discussion can close it.");
            if (post.DiscussionThreadId != null)
            {
                ThreadContext? context = await _Services.Forums.Access.LoadAsync(post.DiscussionThreadId, token).ConfigureAwait(false);
                if (context != null) await _Services.Forums.Threads.DeleteCoreAsync(context, actor.UserId, token).ConfigureAwait(false);
                await _Services.Database.FeedPosts.SetDiscussionAsync(post.Id, null, null, DateTime.UtcNow, token).ConfigureAwait(false);
                await _Services.Audit.WriteAsync(info, "feedPost.undiscuss", TargetTypeEnum.FeedPost.ToString(), post.Id, post.DiscussionThreadId, token).ConfigureAwait(false);
                ForumTelemetry.Count(ForumTelemetry.Promotions, "action", "undiscuss");
            }
            FeedPost stored = (await _Services.Database.FeedPosts.ReadAsync(post.Id, token).ConfigureAwait(false))!;
            return (await _Services.ContentViews.FeedPostsAsync(new[] { stored }, actor, false, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// Publish or withdraw a thread's media on the public paths (public sample threads, section 10).
        /// </summary>
        /// <param name="context">Thread context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task SyncPublicMediaAsync(ThreadContext context, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            bool shouldBePublic = _Services.Property.Property.Features.PublicSamples && ThreadService.IsPublic(context);
            if (shouldBePublic) await _Services.Media.PublishTargetAsync(TargetTypeEnum.Thread, context.Thread.Id, token).ConfigureAwait(false);
            else await _Services.Media.UnpublishTargetAsync(TargetTypeEnum.Thread, context.Thread.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Whether the caller may see a promoted card's thread (feed reads skip cards whose thread they cannot read).
        /// </summary>
        /// <param name="post">Feed post.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True for ordinary posts and for cards whose thread is visible.</returns>
        public async Task<bool> CardVisibleAsync(FeedPost post, ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(post);
            ArgumentNullException.ThrowIfNull(actor);
            if (IsTargetCard(post))
            {
                TargetTypeEnum sourceType = CardSourceType(post);
                ContentTargetProvider? provider = _Services.ContentTargets.Find(sourceType);
                if (provider == null) return false;
                bool moderator = actor.IsSystemAdmin || provider.Moderates(actor, provider.ScopeType(sourceType));
                if (post.SourceHidden && !moderator) return false;
                return await provider.IsVisibleAsync(actor, sourceType, post.SourceId!, token).ConfigureAwait(false);
            }
            if (post.Kind != FeedPostKindEnum.PromotedThread || post.SourceId == null) return true;
            ThreadContext? context = await _Services.Forums.Access.LoadAsync(post.SourceId, token).ConfigureAwait(false);
            return context != null && ForumAccessService.CanSeeThread(context, actor) && (ForumAccessService.StateOf(context.Thread) == ThreadStateEnum.Published || actor.CanModerateForum(context.Thread.ForumId));
        }

        /// <summary>
        /// Decorate feed post payloads: promoted cards take their conversation, counts, and attachments from the thread
        /// (one conversation, section 0.2); posts with a discussion thread carry discussion.
        /// </summary>
        /// <param name="views">Feed post payloads.</param>
        /// <param name="posts">The posts, in the same order.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DecorateAsync(IReadOnlyList<FeedPostView> views, IReadOnlyList<FeedPost> posts, ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(views);
            ArgumentNullException.ThrowIfNull(posts);
            ArgumentNullException.ThrowIfNull(actor);
            await DecorateTargetCardsAsync(views, posts, actor, token).ConfigureAwait(false);
            List<string> threadIds = posts.Where(p => p.Kind == FeedPostKindEnum.PromotedThread && p.SourceId != null).Select(p => p.SourceId!)
                .Concat(posts.Where(p => p.DiscussionThreadId != null).Select(p => p.DiscussionThreadId!)).Distinct(StringComparer.Ordinal).ToList();
            if (threadIds.Count == 0) return;
            Dictionary<string, ForumThread> threads = (await _Services.Database.Threads.ReadManyAsync(threadIds, token).ConfigureAwait(false)).Where(t => t.DeletedUtc == null).ToDictionary(t => t.Id, StringComparer.Ordinal);
            Dictionary<string, Forum> forums = (await _Services.Database.Forums.ReadManyAsync(threads.Values.Select(t => t.ForumId), token).ConfigureAwait(false)).ToDictionary(f => f.Id, StringComparer.Ordinal);
            List<string> promotedIds = posts.Where(p => p.Kind == FeedPostKindEnum.PromotedThread && p.SourceId != null && threads.ContainsKey(p.SourceId)).Select(p => p.SourceId!).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, ReactionState> reactions = await _Services.Reactions.LoadAsync(TargetTypeEnum.Thread, promotedIds, actor.UserId, token).ConfigureAwait(false);
            Dictionary<string, List<MediaView>> media = await _Services.Media.ListViewsForTargetsAsync(TargetTypeEnum.Thread, promotedIds, false, actor.IsStaffFor(TargetTypeEnum.Media), token).ConfigureAwait(false);
            HashSet<string> muted = await _Services.Forums.Access.MutedForumsAsync(actor, forums.Keys, token).ConfigureAwait(false);
            for (int i = 0; i < views.Count && i < posts.Count; i++)
            {
                FeedPostView view = views[i];
                FeedPost post = posts[i];
                if (post.DiscussionThreadId != null && threads.TryGetValue(post.DiscussionThreadId, out ForumThread? discussion) && forums.TryGetValue(discussion.ForumId, out Forum? discussionForum))
                    view.Discussion = new FeedPostDiscussionView { ThreadId = discussion.Id, ForumId = discussionForum.Id, ForumName = discussionForum.Name };
                if (post.Kind != FeedPostKindEnum.PromotedThread || post.SourceId == null || !threads.TryGetValue(post.SourceId, out ForumThread? thread)) continue;
                Forum? forum = forums.TryGetValue(thread.ForumId, out Forum? f) ? f : null;
                ReactionState state = reactions.TryGetValue(thread.Id, out ReactionState? r) ? r : new ReactionState();
                view.CommentTarget = new ContentTarget { Type = TargetTypeEnum.Thread.ToString(), Id = thread.Id };
                view.Source = new FeedPostSourceView { Type = TargetTypeEnum.Thread.ToString(), Id = thread.Id, ForumId = thread.ForumId, ForumName = forum?.Name, Title = thread.Title };
                view.CommentCount = thread.CommentCount;
                view.ReactionCounts = state.ReactionCounts;
                view.ReactionTotal = state.ReactionTotal;
                view.MyReactions = state.MyReactions;
                view.Attachments = (media.TryGetValue(thread.Id, out List<MediaView>? list) ? list : new List<MediaView>()).Take(CardAttachmentMax).ToList();
                bool open = !thread.IsLocked && forum != null && !forum.IsArchived && !muted.Contains(thread.ForumId);
                view.Viewer.CanComment = view.Viewer.CanComment && open;
                view.Viewer.CanReact = view.Viewer.CanReact && !thread.IsLocked && !muted.Contains(thread.ForumId);
            }
        }

        /// <summary>
        /// POST {prefix}/{id}/promote for a content target provider object (Lesson, Event; docs/PHASE4_CONTRACT.md
        /// section 5.8): creates a PromotedLesson or PromotedEvent card whose conversation (commentTarget) is the source.
        /// The audience only narrows; notifyMembers defaults to the provider's default (true for lessons, false for
        /// events); 409 AlreadyPromoted while a card exists. SystemAdmin or canPromoteToFeed.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="targetType">Source type.</param>
        /// <param name="targetId">Source identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The card.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400, 403 PromotionNotAllowed, 404, 409 AlreadyPromoted or PinLimitReached, 429.</exception>
        public async Task<FeedPostView> PromoteTargetAsync(ContentActor actor, RequestInfo info, TargetTypeEnum targetType, string targetId, PromoteRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            actor.RequireUserId();
            if (!actor.IsSystemAdmin && !actor.CanPromoteToFeed) throw new ApiErrorException(403, "PromotionNotAllowed", "Promoting to the feed needs SystemAdmin or canPromoteToFeed.");
            FeedPostKindEnum kind = CardKind(targetType);
            ContentTargetProvider provider = RequireProvider(targetType);
            string id = (targetId ?? String.Empty).Trim();
            PromotionSource source = await provider.PromotionSourceAsync(actor, targetType, id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound(targetType.ToString());
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (!source.IsLive) errors["targetId"] = "Only published content can be promoted.";
            string? title = Override(request.Title, ThreadService.MaxTitleLength, "title", errors);
            string? excerpt = Override(request.Excerpt, MaxExcerptLength, "excerpt", errors);
            ContentAudienceEnum audience = request.Audience ?? source.Audience;
            if (ForumAccessService.Rank(audience) < ForumAccessService.Rank(source.Audience)) errors["audience"] = "The card cannot be broader than its source (" + source.Audience + ").";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (await _Services.Database.FeedPosts.ReadPromotedBySourceAsync(kind, targetType.ToString(), id, token).ConfigureAwait(false) != null)
                throw new ApiErrorException(409, "AlreadyPromoted", "This " + Email.EmailLabels.TargetType(targetType) + " already has a feed card.");
            bool pin = request.Pin == true && actor.IsSystemAdmin;
            if (pin && await _Services.Database.FeedPosts.CountPinnedAsync(token).ConfigureAwait(false) >= _Services.Settings.Feed.MaxPinned)
                throw new ApiErrorException(409, "PinLimitReached", "At most " + _Services.Settings.Feed.MaxPinned + " posts can be pinned. Unpin one first.");
            _Services.Limits.EnforceMember(actor, "forums.promotions", _Services.Settings.Forums.PromotionsPerHour, RateLimitService.Hour);
            DateTime now = DateTime.UtcNow;
            string text = excerpt ?? Cut(source.Excerpt, MaxExcerptLength);
            FeedPost card = await _Services.Database.FeedPosts.CreateAsync(new FeedPost
            {
                Id = IdGenerator.Generate(Constants.FeedPostIdPrefix),
                AuthorUserId = String.IsNullOrEmpty(source.AuthorUserId) ? actor.UserId! : source.AuthorUserId,
                Kind = kind,
                SourceType = targetType.ToString(),
                SourceId = id,
                Title = title ?? Cut(source.Title, ThreadService.MaxTitleLength),
                Body = text,
                Excerpt = text,
                Visibility = audience == ContentAudienceEnum.Public ? FeedVisibilityEnum.Public : FeedVisibilityEnum.Members,
                Audience = audience,
                ShowLinkPreview = false,
                CommentsEnabled = true,
                PublishedUtc = now,
                IsPinned = pin,
                PinnedUtc = pin ? now : null,
                PinnedByUserId = pin ? actor.UserId : null,
                AttachmentCount = Math.Min(source.MediaIds.Count, CardAttachmentMax),
                CreatedUtc = now,
                UpdatedUtc = now
            }, token).ConfigureAwait(false);
            await provider.PromotionChangedAsync(targetType, id, card.Id, token).ConfigureAwait(false);
            if (request.NotifyMembers ?? source.NotifyMembersDefault) await _Services.Notifications.StartFanoutAsync(NotificationTypeEnum.NewFeedPost, card.Id, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, LowerFirst(targetType.ToString()) + ".promote", targetType.ToString(), id, card.Id, token).ConfigureAwait(false);
            ForumTelemetry.Count(ForumTelemetry.Promotions, "action", "promote");
            FeedPost stored = (await _Services.Database.FeedPosts.ReadAsync(card.Id, token).ConfigureAwait(false))!;
            return (await _Services.ContentViews.FeedPostsAsync(new[] { stored }, actor, false, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// POST {prefix}/{id}/unpromote for a content target provider object: deletes its feed card. SystemAdmin, the
        /// member the card is attributed to, or a moderator of the source's scope.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="targetType">Source type.</param>
        /// <param name="targetId">Source identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Promotion state.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound, 429.</exception>
        public async Task<TargetPromotionView> UnpromoteTargetAsync(ContentActor actor, RequestInfo info, TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            FeedPostKindEnum kind = CardKind(targetType);
            ContentTargetProvider provider = RequireProvider(targetType);
            string id = (targetId ?? String.Empty).Trim();
            _ = await provider.PromotionSourceAsync(actor, targetType, id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound(targetType.ToString());
            FeedPost? card = await _Services.Database.FeedPosts.ReadPromotedBySourceAsync(kind, targetType.ToString(), id, token).ConfigureAwait(false);
            bool attributed = card != null && actor.UserId != null && String.Equals(card.AuthorUserId, actor.UserId, StringComparison.Ordinal);
            if (!actor.IsSystemAdmin && !attributed && !provider.Moderates(actor, provider.ScopeType(targetType)))
                throw ApiErrorException.Forbidden("Only a SystemAdmin, the promoter, or a moderator in scope can unpromote this.");
            _Services.Limits.EnforceMember(actor, "forums.promotions", _Services.Settings.Forums.PromotionsPerHour, RateLimitService.Hour);
            TargetPromotionView view = new TargetPromotionView { TargetType = targetType.ToString(), TargetId = id };
            if (card != null)
            {
                await _Services.Feed.DeleteCoreAsync(card, actor.UserId, token).ConfigureAwait(false);
                await provider.PromotionChangedAsync(targetType, id, null, token).ConfigureAwait(false);
                await _Services.Audit.WriteAsync(info, LowerFirst(targetType.ToString()) + ".unpromote", targetType.ToString(), id, card.Id, token).ConfigureAwait(false);
                ForumTelemetry.Count(ForumTelemetry.Promotions, "action", "unpromote");
                view.Unpromoted = true;
            }
            return view;
        }

        /// <summary>
        /// Re-check the feed card of a content target provider object after the object changed (moderation state,
        /// publish, archive, cancel, delete): the card is hidden while the source is not live and deleted when the
        /// source is gone (the provider's <see cref="ContentTargetProvider.PromotionSourceAsync"/> returns null or 404
        /// for a SystemAdmin). Moderation actions call it; feature modules call it after their own lifecycle changes.
        /// Does nothing for types without cards.
        /// </summary>
        /// <param name="targetType">Source type.</param>
        /// <param name="targetId">Source identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task SyncTargetAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (targetType != TargetTypeEnum.Lesson && targetType != TargetTypeEnum.Event) return;
            ContentTargetProvider? provider = _Services.ContentTargets.Find(targetType);
            if (provider == null || String.IsNullOrWhiteSpace(targetId)) return;
            FeedPost? card = await _Services.Database.FeedPosts.ReadPromotedBySourceAsync(CardKind(targetType), targetType.ToString(), targetId, token).ConfigureAwait(false);
            if (card == null) return;
            PromotionSource? source;
            try
            {
                source = await provider.PromotionSourceAsync(SystemActor(), targetType, targetId, token).ConfigureAwait(false);
            }
            catch (ApiErrorException e) when (e.StatusCode == 404)
            {
                source = null;
            }
            if (source == null)
            {
                await _Services.Feed.DeleteCoreAsync(card, null, token).ConfigureAwait(false);
                await provider.PromotionChangedAsync(targetType, targetId, null, token).ConfigureAwait(false);
                return;
            }
            if (card.SourceHidden == source.IsLive) await _Services.Database.FeedPosts.SetSourceHiddenAsync(card.Id, !source.IsLive, DateTime.UtcNow, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static FeedPostKindEnum CardKind(TargetTypeEnum targetType)
        {
            if (targetType == TargetTypeEnum.Lesson) return FeedPostKindEnum.PromotedLesson;
            if (targetType == TargetTypeEnum.Event) return FeedPostKindEnum.PromotedEvent;
            throw ApiErrorException.NotFound(targetType.ToString());
        }

        private static bool IsTargetCard(FeedPost post)
        {
            return (post.Kind == FeedPostKindEnum.PromotedLesson || post.Kind == FeedPostKindEnum.PromotedEvent) && post.SourceId != null && post.SourceType != null;
        }

        private static TargetTypeEnum CardSourceType(FeedPost post)
        {
            return post.Kind == FeedPostKindEnum.PromotedLesson ? TargetTypeEnum.Lesson : TargetTypeEnum.Event;
        }

        private ContentTargetProvider RequireProvider(TargetTypeEnum targetType)
        {
            return _Services.ContentTargets.Find(targetType) ?? throw ApiErrorException.NotFound(targetType.ToString());
        }

        private static ContentActor SystemActor()
        {
            return new ContentActor { IsBreakGlass = true, IsSystemAdmin = true, IsStaff = true, HasMembership = true, AgeAttested = true };
        }

        private static string LowerFirst(string value)
        {
            return value.Length == 0 ? value : Char.ToLowerInvariant(value[0]) + value.Substring(1);
        }

        private async Task DecorateTargetCardsAsync(IReadOnlyList<FeedPostView> views, IReadOnlyList<FeedPost> posts, ContentActor actor, CancellationToken token)
        {
            List<int> indexes = new List<int>();
            for (int i = 0; i < views.Count && i < posts.Count; i++) if (IsTargetCard(posts[i])) indexes.Add(i);
            if (indexes.Count == 0) return;
            foreach (IGrouping<TargetTypeEnum, int> group in indexes.GroupBy(i => CardSourceType(posts[i])))
            {
                ContentTargetProvider? provider = _Services.ContentTargets.Find(group.Key);
                if (provider == null) continue;
                List<string> ids = group.Select(i => posts[i].SourceId!).Distinct(StringComparer.Ordinal).ToList();
                Dictionary<string, ReactionState> reactions = await _Services.Reactions.LoadAsync(group.Key, ids, actor.UserId, token).ConfigureAwait(false);
                Dictionary<string, List<MediaView>> media = await _Services.Media.ListViewsForTargetsAsync(group.Key, ids, false, actor.IsStaffFor(TargetTypeEnum.Media), token).ConfigureAwait(false);
                foreach (int i in group)
                {
                    FeedPostView view = views[i];
                    string sourceId = posts[i].SourceId!;
                    PromotionSource? source = null;
                    try
                    {
                        source = await provider.PromotionSourceAsync(actor, group.Key, sourceId, token).ConfigureAwait(false);
                    }
                    catch (ApiErrorException)
                    {
                        source = null;
                    }
                    ReactionState state = reactions.TryGetValue(sourceId, out ReactionState? r) ? r : new ReactionState();
                    view.CommentTarget = new ContentTarget { Type = group.Key.ToString(), Id = sourceId };
                    view.Source = new FeedPostSourceView { Type = group.Key.ToString(), Id = sourceId, Title = source?.Title ?? posts[i].Title };
                    view.CommentCount = await provider.CommentCountAsync(group.Key, sourceId, token).ConfigureAwait(false);
                    view.ReactionCounts = state.ReactionCounts;
                    view.ReactionTotal = state.ReactionTotal;
                    view.MyReactions = state.MyReactions;
                    List<MediaView> attached = media.TryGetValue(sourceId, out List<MediaView>? list) ? list : new List<MediaView>();
                    if (source != null && source.MediaIds.Count > 0)
                        attached = attached.OrderBy(m => source.MediaIds.IndexOf(m.Id) < 0 ? Int32.MaxValue : source.MediaIds.IndexOf(m.Id)).ToList();
                    view.Attachments = attached.Take(CardAttachmentMax).ToList();
                    view.Viewer.CanComment = view.Viewer.CanComment && source != null && source.IsLive && source.CommentsOpen;
                    view.Viewer.CanReact = view.Viewer.CanReact && source != null && source.IsLive && source.ReactionsOpen;
                }
            }
        }

        private static void RequirePromoter(ContentActor actor)
        {
            if (actor.IsSystemAdmin || actor.CanPromoteToFeed) return;
            throw new ApiErrorException(403, "PromotionNotAllowed", "Promoting threads to the feed needs SystemAdmin or canPromoteToFeed.");
        }

        private static string? Override(string? value, int max, string field, Dictionary<string, string> errors)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            if (!MarkdownNormalizer.IsWellFormed(value))
            {
                errors[field] = "Must be valid UTF-8 text.";
                return null;
            }
            string normalized = MarkdownNormalizer.NormalizeSingleLine(value);
            int length = MarkdownNormalizer.Length(normalized);
            if (field == "title" && (length < ThreadService.MinTitleLength || length > max)) errors[field] = "Use " + ThreadService.MinTitleLength + " to " + max + " characters.";
            else if (length > max) errors[field] = "At most " + max + " characters.";
            return normalized;
        }

        private static string Cut(string value, int max)
        {
            string text = (value ?? String.Empty).Trim();
            return text.Length > max ? text.Substring(0, max).Trim() : text;
        }

        private static string Summary(ForumThread thread)
        {
            return ThreadService.Summary(thread);
        }

        #endregion
    }
}
