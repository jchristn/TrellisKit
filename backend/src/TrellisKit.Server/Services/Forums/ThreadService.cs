namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
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
    /// Forum threads: lists, read, create, edit history, delete, pin, lock, move, accepted answers, feedback status,
    /// follows, publishing, admin and public lists (docs/PHASE3_CONTRACT.md sections 4, 6.4, 7, 9.2, 9.4, and 10).
    /// </summary>
    public class ThreadService
    {
        #region Public-Members

        /// <summary>
        /// Shortest thread title.
        /// </summary>
        public const int MinTitleLength = 3;

        /// <summary>
        /// Longest thread title.
        /// </summary>
        public const int MaxTitleLength = 140;

        /// <summary>
        /// Longest lock reason.
        /// </summary>
        public const int MaxLockReasonLength = 500;

        /// <summary>
        /// Longest feedback focus.
        /// </summary>
        public const int MaxFocusLength = 500;

        /// <summary>
        /// Most forums in a forumIds filter.
        /// </summary>
        public const int MaxForumIds = 50;

        /// <summary>
        /// Most public sample threads per page.
        /// </summary>
        public const int MaxPublicResults = 20;

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
        public ThreadService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        #region Lists

        /// <summary>
        /// POST /api/v1/forums/{id}/threads/enumerate: a forum's threads, the first page led by its pinned threads.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="forumId">Forum identifier or slug.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of thread list items.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound, 429 RateLimited.</exception>
        public async Task<EnumerationResult<ThreadSummaryView>> ListForumAsync(ContentActor actor, string forumId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            Forum forum = await _Services.Forums.Access.RequireForumAsync(actor, forumId, token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (String.IsNullOrWhiteSpace(q.Ordering) && String.IsNullOrWhiteSpace(q.Search)) q.Ordering = forum.DefaultOrdering.ToString();
            EnumerationResult<ThreadSummaryView> page = await ListCoreAsync(actor, q, ForumCollections.ForumThreads, forum, "forum|" + forum.Id, token).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(actor.UserId) && String.IsNullOrWhiteSpace(q.ContinuationToken) && q.Skip == 0)
                await _Services.Database.Forums.MarkReadAsync(actor.UserId, forum.Id, DateTime.UtcNow, token).ConfigureAwait(false);
            return page;
        }

        /// <summary>
        /// POST /api/v1/threads/enumerate: threads across forums (All forums, own threads, Following, and search).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of thread list items.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 429 RateLimited.</exception>
        public Task<EnumerationResult<ThreadSummaryView>> ListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Forums.Access.RequireEnabled();
            _Services.Media.EnforceRead(actor);
            return ListCoreAsync(actor, query ?? new EnumerationQuery(), ForumCollections.Threads, null, "threads", token);
        }

        /// <summary>
        /// POST /api/v1/admin/threads/enumerate: threads in every state in the caller's moderation scope.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin or a forum moderator).</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of thread list items.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<EnumerationResult<ThreadSummaryView>> ListAdminAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Forums.Access.RequireEnabled();
            if (!actor.ModeratesAnyForum) throw ApiErrorException.Forbidden("A forum moderator role is required.");
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            ThreadFilter filter = new ThreadFilter
            {
                ForumIds = ForumIdsFilter(q, errors),
                ScopeForumIds = actor.ForumScope,
                Types = ForumFilters.Enums<ThreadTypeEnum>(q, "type", errors),
                FeedbackStatuses = ForumFilters.Enums<FeedbackStatusEnum>(q, "feedbackStatus", errors),
                AuthorUserId = q.FilterString("authorUserId"),
                Pinned = q.FilterBool("pinned", errors),
                Locked = q.FilterBool("locked", errors),
                Promoted = q.FilterBool("promoted", errors),
                AudienceFilter = ForumFilters.Enums<ContentAudienceEnum>(q, "audience", errors),
                NowUtc = DateTime.UtcNow
            };
            List<ThreadStateEnum> states = ForumFilters.Enums<ThreadStateEnum>(q, "state", errors);
            filter.States = states.Count > 0 ? states : new List<ThreadStateEnum> { ThreadStateEnum.Processing, ThreadStateEnum.Published, ThreadStateEnum.Hidden, ThreadStateEnum.Removed };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ForumCollections.AdminThreads, true, "admin-threads|" + ScopeKey(actor));
            EnumerationResult<ForumThread> page = await _Services.Database.Threads.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            List<ThreadContext> contexts = await _Services.Forums.Access.ContextsAsync(page.Objects, token).ConfigureAwait(false);
            return page.With(await _Services.Forums.Views.SummariesAsync(contexts, actor, true, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/public/threads/enumerate: Published Public threads in Public forums (anonymous).
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="sourceIp">Caller address (rate limit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of public threads.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotAvailable, 429 RateLimited.</exception>
        public async Task<EnumerationResult<PublicThreadView>> ListPublicAsync(EnumerationQuery? query, string? sourceIp, CancellationToken token = default)
        {
            RequirePublic(sourceIp);
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (q.MaxResults == null || q.MaxResults > MaxPublicResults) q.MaxResults = Math.Min(q.MaxResults ?? MaxPublicResults, MaxPublicResults);
            if (!String.IsNullOrWhiteSpace(q.Ordering) && !String.Equals(q.Ordering.Trim(), "CreatedDescending", StringComparison.OrdinalIgnoreCase))
                throw ApiErrorException.Field("ordering", "Use CreatedDescending.");
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ForumCollections.PublicThreads, false, "public-threads", MaxPublicResults);
            ThreadFilter filter = new ThreadFilter { ForumIds = await PublicForumIdsAsync(token).ConfigureAwait(false), Audiences = new List<ContentAudienceEnum> { ContentAudienceEnum.Public }, NowUtc = DateTime.UtcNow };
            if (filter.ForumIds.Count == 0) return new EnumerationResult<PublicThreadView> { MaxResults = request.MaxResults, Skip = request.Skip, TotalRecords = 0, RecordsRemaining = 0 };
            EnumerationResult<ForumThread> page = await _Services.Database.Threads.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            List<ThreadContext> contexts = (await _Services.Forums.Access.ContextsAsync(page.Objects, token).ConfigureAwait(false)).Where(c => c.Thread.Kind == ThreadKindEnum.Original).ToList();
            return page.With(await _Services.Forums.Views.PublicAsync(contexts, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/public/threads/{id} (anonymous).
        /// </summary>
        /// <param name="threadId">Thread.</param>
        /// <param name="sourceIp">Caller address (rate limit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Public thread.</returns>
        /// <exception cref="ApiErrorException">404 NotFound, 429 RateLimited.</exception>
        public async Task<PublicThreadView> GetPublicAsync(string threadId, string? sourceIp, CancellationToken token = default)
        {
            RequirePublic(sourceIp);
            ThreadContext? context = await _Services.Forums.Access.LoadAsync(threadId, token).ConfigureAwait(false);
            if (context == null || !IsPublic(context)) throw ApiErrorException.NotFound("Thread");
            return (await _Services.Forums.Views.PublicAsync(new[] { context }, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// Whether a thread is a public sample (Published, Public, in a Public forum, authored in the forum).
        /// </summary>
        /// <param name="context">Thread context.</param>
        /// <returns>True when public.</returns>
        public static bool IsPublic(ThreadContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return ForumAccessService.StateOf(context.Thread) == ThreadStateEnum.Published && context.Thread.Kind == ThreadKindEnum.Original &&
                context.Thread.Audience == ContentAudienceEnum.Public && context.Forum.Audience == ContentAudienceEnum.Public && context.Forum.DeletedUtc == null;
        }

        #endregion

        #region Read-and-Write

        /// <summary>
        /// GET /api/v1/threads/{id}; records the caller's read position.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="threadId">Thread.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Thread.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<ThreadView> GetAsync(ContentActor actor, string threadId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            ThreadContext context = await _Services.Forums.Access.RequireThreadAsync(actor, threadId, token).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(actor.UserId)) await _Services.Database.Threads.MarkReadAsync(actor.UserId, context.Thread.Id, DateTime.UtcNow, token).ConfigureAwait(false);
            return await ViewAsync(context, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Build one thread payload.
        /// </summary>
        /// <param name="context">Thread context.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<ThreadView> ViewAsync(ThreadContext context, ContentActor actor, CancellationToken token = default)
        {
            return (await _Services.Forums.Views.ThreadsAsync(new[] { context }, actor, false, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// Reload a thread and build its payload.
        /// </summary>
        /// <param name="threadId">Thread.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<ThreadView> ReloadAsync(string threadId, ContentActor actor, CancellationToken token = default)
        {
            ThreadContext? context = await _Services.Forums.Access.LoadAsync(threadId, token).ConfigureAwait(false);
            if (context == null) throw ApiErrorException.NotFound("Thread");
            return await ViewAsync(context, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/forums/{id}/threads.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="forumId">Forum.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The new thread (Processing or Published).</returns>
        /// <exception cref="ApiErrorException">400, 403, 404, 409, or 429 per the contract.</exception>
        public async Task<ThreadView> CreateAsync(ContentActor actor, RequestInfo info, string forumId, ThreadRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            string userId = actor.RequireUserId();
            using (Activity? activity = TrellisKitTelemetry.StartSpan("forums.create"))
            {
                try
                {
                    ThreadView view = await CreateCoreAsync(actor, userId, info, forumId, request, token).ConfigureAwait(false);
                    await new Metrics.ProductMetricEventService(_Services).ObserveAsync("member.firstAction", userId, token).ConfigureAwait(false);
                    ForumTelemetry.Count(ForumTelemetry.Threads, "action", "create", "type", view.Type.ToString(), "outcome", "success");
                    activity?.SetTag("thread.id", view.Id);
                    activity?.SetTag("forum.id", view.Forum.Id);
                    activity?.SetTag("thread.type", view.Type.ToString());
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return view;
                }
                catch (ApiErrorException)
                {
                    ForumTelemetry.Count(ForumTelemetry.Threads, "action", "create", "type", request.Type?.ToString() ?? "unknown", "outcome", "rejected");
                    throw;
                }
            }
        }

        /// <summary>
        /// PUT /api/v1/threads/{id}: author edits (revision history after the grace period) and moderator metadata edits.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="threadId">Thread.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Fields present in the body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Thread.</returns>
        /// <exception cref="ApiErrorException">400, 403, 404, 409, or 429 per the contract.</exception>
        public async Task<ThreadView> UpdateAsync(ContentActor actor, RequestInfo info, string threadId, ThreadRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            try
            {
                ThreadView view = await UpdateCoreAsync(actor, info, threadId, request, present, token).ConfigureAwait(false);
                ForumTelemetry.Count(ForumTelemetry.Threads, "action", "update", "type", view.Type.ToString(), "outcome", "success");
                return view;
            }
            catch (ApiErrorException)
            {
                ForumTelemetry.Count(ForumTelemetry.Threads, "action", "update", "type", "unknown", "outcome", "rejected");
                throw;
            }
        }

        /// <summary>
        /// DELETE /api/v1/threads/{id}: the author, a moderator in scope (resolved decision 1), or SystemAdmin.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="threadId">Thread.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task DeleteAsync(ContentActor actor, RequestInfo info, string threadId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ThreadContext context = await _Services.Forums.Access.RequireThreadAsync(actor, threadId, token).ConfigureAwait(false);
            bool isAuthor = IsAuthor(actor, context.Thread);
            bool authorIsAdmin = await _Services.Forums.Access.IsSystemAdminAsync(context.Thread.AuthorUserId, token).ConfigureAwait(false);
            if (!isAuthor && !ForumAccessService.CanModerate(actor, context.Thread.ForumId, authorIsAdmin)) throw ApiErrorException.Forbidden("Only the author or a moderator in scope can delete this thread.");
            await DeleteCoreAsync(context, actor.UserId, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "thread.delete", TargetTypeEnum.Thread.ToString(), context.Thread.Id, Summary(context.Thread), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Soft-delete a thread with its coupling rules: pin cleared, follows deleted, promoted card removed, the feed
        /// post's discussion link cleared, public media withdrawn, and the search entry removed.
        /// </summary>
        /// <param name="context">Thread context.</param>
        /// <param name="byUserId">Who deleted it.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted now.</returns>
        public async Task<bool> DeleteCoreAsync(ThreadContext context, string? byUserId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            using (Activity? activity = TrellisKitTelemetry.StartSpan("forums.delete"))
            {
                activity?.SetTag("thread.id", context.Thread.Id);
                bool deleted = await _Services.Database.Threads.SoftDeleteAsync(context.Thread.Id, byUserId, DateTime.UtcNow, token).ConfigureAwait(false);
                if (!deleted) return false;
                await _Services.Favorites.TargetDeletedAsync(FavoriteTargetTypeEnum.Thread, context.Thread.Id, token).ConfigureAwait(false);
                await _Services.Forums.Promotions.EndPromotionAsync(context.Thread, token).ConfigureAwait(false);
                if (context.Thread.Kind == ThreadKindEnum.LinkedFeedPost && context.Thread.SourceId != null)
                    await _Services.Database.FeedPosts.SetDiscussionAsync(context.Thread.SourceId, null, null, DateTime.UtcNow, token).ConfigureAwait(false);
                await _Services.Media.UnpublishTargetAsync(TargetTypeEnum.Thread, context.Thread.Id, token).ConfigureAwait(false);
                await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Thread, context.Thread.Id, token).ConfigureAwait(false);
                ForumTelemetry.Count(ForumTelemetry.Threads, "action", "delete", "type", context.Thread.Type.ToString(), "outcome", "success");
                activity?.SetStatus(ActivityStatusCode.Ok);
                return true;
            }
        }

        /// <summary>
        /// POST /api/v1/threads/{id}/revisions/enumerate: newest first; visibility follows the thread.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="threadId">Thread.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of revisions.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task<EnumerationResult<ThreadRevisionView>> RevisionsAsync(ContentActor actor, string threadId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            ThreadContext context = await _Services.Forums.Access.RequireThreadAsync(actor, threadId, token).ConfigureAwait(false);
            return await RevisionPageAsync(context.Thread.Id, query, actor.IsStaffFor(TargetTypeEnum.ThreadRevision), 50, token).ConfigureAwait(false);
        }

        /// <summary>
        /// A page of a thread's revisions.
        /// </summary>
        /// <param name="threadId">Thread.</param>
        /// <param name="query">Query.</param>
        /// <param name="staff">Whether staff metadata is included.</param>
        /// <param name="cap">Largest page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of revisions.</returns>
        public async Task<EnumerationResult<ThreadRevisionView>> RevisionPageAsync(string threadId, EnumerationQuery? query, bool staff, int cap, CancellationToken token = default)
        {
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (q.MaxResults == null) q.MaxResults = 20;
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ForumCollections.ThreadRevisions, staff, "thread-revisions|" + threadId, cap);
            EnumerationResult<ThreadRevision> page = await _Services.Database.Threads.EnumerateRevisionsAsync(threadId, request, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> editors = await _Services.Summaries.BuildAsync(page.Objects.Select(r => r.EditedByUserId), token).ConfigureAwait(false);
            List<ThreadRevisionView> views = page.Objects.Select(r => new ThreadRevisionView
            {
                Id = r.Id,
                Revision = r.Revision,
                Title = r.Title,
                Body = r.Body,
                Type = r.Type,
                AttachmentSummary = r.AttachmentSummary,
                EditedBy = r.EditedByUserId != null && editors.TryGetValue(r.EditedByUserId, out UserSummary? u) ? u : UserSummary.Deleted(),
                EditedByRole = r.EditedByRole,
                CreatedUtc = r.CreatedUtc
            }).ToList();
            return page.With(await _Services.Metadata.AttachAsync(TargetTypeEnum.ThreadRevision, views, v => v.Id, staff, token).ConfigureAwait(false));
        }

        #endregion

        #region Moderation

        /// <summary>
        /// POST /api/v1/threads/{id}/pin and .../unpin (moderator in scope; Published threads only; idempotent).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="threadId">Thread.</param>
        /// <param name="pin">Pin (true) or unpin (false).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Thread.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound, 409 PinLimitReached or NotPublished.</exception>
        public async Task<ThreadView> SetPinnedAsync(ContentActor actor, RequestInfo info, string threadId, bool pin, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ThreadContext context = await RequireModeratedAsync(actor, threadId, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            string action = pin ? "pin" : "unpin";
            if (pin)
            {
                if (ForumAccessService.StateOf(context.Thread) != ThreadStateEnum.Published) throw new ApiErrorException(409, "NotPublished", "Only published threads can be pinned.");
                if (!context.Thread.IsPinned && !await _Services.Database.Threads.PinAsync(context.Thread.Id, actor.UserId, now, _Services.Settings.Forums.MaxPinnedPerForum, token).ConfigureAwait(false))
                {
                    ForumTelemetry.Count(ForumTelemetry.Threads, "action", action, "type", context.Thread.Type.ToString(), "outcome", "rejected");
                    throw new ApiErrorException(409, "PinLimitReached", "At most " + _Services.Settings.Forums.MaxPinnedPerForum + " threads can be pinned in a forum. Unpin one first.");
                }
            }
            else if (context.Thread.IsPinned)
            {
                await _Services.Database.Threads.UnpinAsync(context.Thread.Id, now, token).ConfigureAwait(false);
            }
            await _Services.Forums.Moderation.RecordAsync(actor, context.Thread, pin ? ModerationActionTypeEnum.Pin : ModerationActionTypeEnum.Unpin, pin ? "Pinned" : "Unpinned", null, null, null, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "thread." + action, TargetTypeEnum.Thread.ToString(), context.Thread.Id, Summary(context.Thread), token).ConfigureAwait(false);
            ForumTelemetry.Count(ForumTelemetry.Threads, "action", action, "type", context.Thread.Type.ToString(), "outcome", "success");
            return await ReloadAsync(context.Thread.Id, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/threads/{id}/lock and .../unlock (moderator in scope; idempotent).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="threadId">Thread.</param>
        /// <param name="locked">Lock (true) or unlock (false).</param>
        /// <param name="reason">Lock reason (0 to 500 characters).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Thread.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<ThreadView> SetLockedAsync(ContentActor actor, RequestInfo info, string threadId, bool locked, string? reason, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            string? text = String.IsNullOrWhiteSpace(reason) ? null : MarkdownNormalizer.NormalizeSingleLine(reason);
            if (text != null && MarkdownNormalizer.Length(text) > MaxLockReasonLength) throw ApiErrorException.Field("reason", "At most " + MaxLockReasonLength + " characters.");
            ThreadContext context = await RequireModeratedAsync(actor, threadId, token).ConfigureAwait(false);
            string action = locked ? "lock" : "unlock";
            await _Services.Database.Threads.SetLockedAsync(context.Thread.Id, locked, actor.UserId, text, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Forums.Moderation.RecordAsync(actor, context.Thread, locked ? ModerationActionTypeEnum.Lock : ModerationActionTypeEnum.Unlock, text ?? (locked ? "Locked" : "Unlocked"), null, null, null, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "thread." + action, TargetTypeEnum.Thread.ToString(), context.Thread.Id, Summary(context.Thread), token).ConfigureAwait(false);
            ForumTelemetry.Count(ForumTelemetry.Threads, "action", action, "type", context.Thread.Type.ToString(), "outcome", "success");
            return await ReloadAsync(context.Thread.Id, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/threads/{id}/move (moderator in scope of both forums).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="threadId">Thread.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Thread.</returns>
        /// <exception cref="ApiErrorException">400 ThreadTypeNotAllowed or ValidationFailed, 403 Forbidden, 404 NotFound, 409 ForumArchived.</exception>
        public async Task<ThreadView> MoveAsync(ContentActor actor, RequestInfo info, string threadId, ThreadMoveRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            if (String.IsNullOrWhiteSpace(request.ForumId)) throw ApiErrorException.Field("forumId", "Required.");
            using (Activity? activity = TrellisKitTelemetry.StartSpan("forums.move"))
            {
                ThreadContext context = await RequireModeratedAsync(actor, threadId, token).ConfigureAwait(false);
                activity?.SetTag("thread.id", context.Thread.Id);
                Forum? destination = await _Services.Database.Forums.ReadAsync(request.ForumId.Trim(), token).ConfigureAwait(false);
                if (destination == null || !ForumAccessService.CanReadForum(destination, actor)) throw ApiErrorException.NotFound("Forum");
                if (!actor.CanModerateForum(destination.Id)) throw ApiErrorException.Forbidden("You do not moderate the destination forum.");
                if (destination.IsArchived) throw new ApiErrorException(409, "ForumArchived", "The destination forum is archived.");
                if (!destination.AllowedThreadTypes.Contains(context.Thread.Type)) throw new ApiErrorException(400, "ThreadTypeNotAllowed", "The destination forum does not allow " + context.Thread.Type + " threads.");
                if (String.Equals(destination.Id, context.Thread.ForumId, StringComparison.Ordinal)) return await ReloadAsync(context.Thread.Id, actor, token).ConfigureAwait(false);
                string? reason = String.IsNullOrWhiteSpace(request.Reason) ? null : MarkdownNormalizer.NormalizeSingleLine(request.Reason);
                if (reason != null && MarkdownNormalizer.Length(reason) > ModerationService.MaxReasonLength) throw ApiErrorException.Field("reason", "At most " + ModerationService.MaxReasonLength + " characters.");
                ContentAudienceEnum audience = ForumAccessService.Narrowest(context.Thread.Audience, destination.Audience);
                await _Services.Database.Threads.MoveAsync(context.Thread.Id, destination.Id, audience, DateTime.UtcNow, token).ConfigureAwait(false);
                await _Services.Forums.Moderation.RecordAsync(actor, context.Thread, ModerationActionTypeEnum.Move, reason ?? "Moved to " + destination.Name, context.Thread.ForumId, destination.Id, null, token).ConfigureAwait(false);
                await _Services.Audit.WriteAsync(info, "thread.move", TargetTypeEnum.Thread.ToString(), context.Thread.Id, context.Forum.Name + " to " + destination.Name, token).ConfigureAwait(false);
                if (request.NotifyAuthor ?? true) await _Services.Forums.Notifications.ThreadMovedAsync(context.Thread, destination, reason, token).ConfigureAwait(false);
                await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Thread, context.Thread.Id, token).ConfigureAwait(false);
                ForumTelemetry.Count(ForumTelemetry.Threads, "action", "move", "type", context.Thread.Type.ToString(), "outcome", "success");
                ThreadContext moved = (await _Services.Forums.Access.LoadAsync(context.Thread.Id, token).ConfigureAwait(false))!;
                await _Services.Forums.Promotions.SyncPublicMediaAsync(moved, token).ConfigureAwait(false);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return await ViewAsync(moved, actor, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// POST /api/v1/threads/{id}/accept-answer (thread author or moderator in scope; Question threads).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="threadId">Thread.</param>
        /// <param name="commentId">Comment to accept, or null to clear.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Thread.</returns>
        /// <exception cref="ApiErrorException">400 InvalidAcceptedAnswer, 403 Forbidden, 404 NotFound.</exception>
        public async Task<ThreadView> SetAcceptedAnswerAsync(ContentActor actor, RequestInfo info, string threadId, string? commentId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ThreadContext context = await _Services.Forums.Access.RequireThreadAsync(actor, threadId, token).ConfigureAwait(false);
            ForumThread thread = context.Thread;
            bool isAuthor = IsAuthor(actor, thread);
            bool authorIsAdmin = await _Services.Forums.Access.IsSystemAdminAsync(thread.AuthorUserId, token).ConfigureAwait(false);
            bool moderator = ForumAccessService.CanModerate(actor, thread.ForumId, authorIsAdmin);
            if (!isAuthor && !moderator) throw ApiErrorException.Forbidden("Only the thread author or a moderator in scope can choose the accepted answer.");
            if (thread.Type != ThreadTypeEnum.Question) throw new ApiErrorException(400, "InvalidAcceptedAnswer", "Only Question threads have an accepted answer.");
            Comment? comment = null;
            if (commentId != null)
            {
                comment = String.IsNullOrWhiteSpace(commentId) ? null : await _Services.Database.Comments.ReadAsync(commentId.Trim(), token).ConfigureAwait(false);
                if (comment == null || comment.TargetType != TargetTypeEnum.Thread || !String.Equals(comment.TargetId, thread.Id, StringComparison.Ordinal)
                    || comment.DeletedUtc != null || comment.ModerationState != ModerationStateEnum.None)
                    throw new ApiErrorException(400, "InvalidAcceptedAnswer", "Choose a visible comment of this thread.");
            }
            if (!await _Services.Database.Threads.SetAcceptedAnswerAsync(thread.Id, comment?.Id, DateTime.UtcNow, token).ConfigureAwait(false))
                throw new ApiErrorException(400, "InvalidAcceptedAnswer", "Choose a visible comment of this thread.");
            string action = comment == null ? "clear" : "accept";
            ForumTelemetry.Count(ForumTelemetry.Answers, "action", action);
            await _Services.Audit.WriteAsync(info, comment == null ? "thread.clearAnswer" : "thread.acceptAnswer", TargetTypeEnum.Thread.ToString(), thread.Id, comment?.Id ?? Summary(thread), token).ConfigureAwait(false);
            if (moderator && !isAuthor) await _Services.Forums.Moderation.RecordAsync(actor, thread, ModerationActionTypeEnum.EditMetadata, comment == null ? "Cleared the accepted answer" : "Accepted an answer", null, null, null, token).ConfigureAwait(false);
            if (comment != null) await _Services.Forums.Notifications.AnswerAcceptedAsync(actor, thread, comment, token).ConfigureAwait(false);
            return await ReloadAsync(thread.Id, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/threads/{id}/feedback: moderators in scope for every transition; the author may set Closed.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="threadId">Thread.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Thread.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<ThreadView> SetFeedbackAsync(ContentActor actor, RequestInfo info, string threadId, FeedbackStatusRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            if (request.Status == null) throw ApiErrorException.Field("status", "Use Open, InReview, Reviewed, or Closed.");
            ThreadContext context = await _Services.Forums.Access.RequireThreadAsync(actor, threadId, token).ConfigureAwait(false);
            ForumThread thread = context.Thread;
            if (thread.Type != ThreadTypeEnum.Feedback) throw ApiErrorException.Field("status", "Only Feedback threads have a review status.");
            bool isAuthor = IsAuthor(actor, thread);
            bool authorIsAdmin = await _Services.Forums.Access.IsSystemAdminAsync(thread.AuthorUserId, token).ConfigureAwait(false);
            bool reviewer = ForumAccessService.CanModerate(actor, thread.ForumId, authorIsAdmin);
            FeedbackStatusEnum status = request.Status.Value;
            if (!reviewer && !(isAuthor && status == FeedbackStatusEnum.Closed)) throw ApiErrorException.Forbidden("Only reviewers in scope can change this status; the author may close the request.");
            string? reviewComment = null;
            if (status == FeedbackStatusEnum.Reviewed)
            {
                Comment? comment = String.IsNullOrWhiteSpace(request.ReviewCommentId) ? null : await _Services.Database.Comments.ReadAsync(request.ReviewCommentId.Trim(), token).ConfigureAwait(false);
                if (comment == null || comment.TargetType != TargetTypeEnum.Thread || !String.Equals(comment.TargetId, thread.Id, StringComparison.Ordinal)
                    || comment.DeletedUtc != null || comment.ModerationState != ModerationStateEnum.None)
                    throw ApiErrorException.Field("reviewCommentId", "Choose a visible comment of this thread as the review.");
                reviewComment = comment.Id;
            }
            if (status == FeedbackStatusEnum.Open || status == FeedbackStatusEnum.InReview)
            {
                bool reopening = thread.FeedbackStatus == FeedbackStatusEnum.Closed || thread.FeedbackStatus == FeedbackStatusEnum.Reviewed;
                if (reopening) await EnforceFeedbackLimitAsync(context.Forum, thread.AuthorUserId, token).ConfigureAwait(false);
            }
            string? reviewerId = status == FeedbackStatusEnum.Open ? null : (reviewer && status != FeedbackStatusEnum.Closed ? actor.UserId : thread.FeedbackReviewerId);
            if (!await _Services.Database.Threads.SetFeedbackAsync(thread.Id, status, reviewerId, reviewComment, DateTime.UtcNow, token).ConfigureAwait(false))
                throw ApiErrorException.Field("reviewCommentId", "Choose a visible comment of this thread as the review.");
            ForumTelemetry.Count(ForumTelemetry.Feedback, "status", status.ToString());
            await _Services.Audit.WriteAsync(info, "thread.feedback", TargetTypeEnum.Thread.ToString(), thread.Id, status.ToString(), token).ConfigureAwait(false);
            if (status == FeedbackStatusEnum.Reviewed && reviewComment != null) await _Services.Forums.Notifications.FeedbackReviewedAsync(actor, thread, reviewComment, token).ConfigureAwait(false);
            await RefreshReviewGaugeAsync(token).ConfigureAwait(false);
            return await ReloadAsync(thread.Id, actor, token).ConfigureAwait(false);
        }

        #endregion

        #region Follows

        /// <summary>
        /// PUT /api/v1/threads/{id}/follow and DELETE (unfollowing sticks against automatic follows).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="threadId">Thread.</param>
        /// <param name="follow">Follow (true) or unfollow (false).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Follow state.</returns>
        /// <exception cref="ApiErrorException">404 NotFound, 429 RateLimited.</exception>
        public async Task<FollowStateResponse> SetFollowAsync(ContentActor actor, string threadId, bool follow, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            ThreadContext context = await _Services.Forums.Access.RequireThreadAsync(actor, threadId, token).ConfigureAwait(false);
            _Services.Limits.EnforceMember(actor, "forums.follows", _Services.Settings.Forums.FollowsPerMinute, RateLimitService.Minute);
            DateTime now = DateTime.UtcNow;
            if (follow)
            {
                await _Services.Database.Threads.FollowAsync(new ThreadFollow { Id = IdGenerator.Generate(ForumIdPrefixes.ThreadFollow), ThreadId = context.Thread.Id, UserId = userId, Source = FollowSourceEnum.Explicit, CreatedUtc = now },
                    _Services.Settings.Forums.MaxFollowsPerMember, token).ConfigureAwait(false);
            }
            else
            {
                await _Services.Database.Threads.UnfollowAsync(context.Thread.Id, userId, now, token).ConfigureAwait(false);
            }
            ForumTelemetry.Count(ForumTelemetry.Follows, "target_type", "thread", "action", follow ? "follow" : "unfollow");
            return new FollowStateResponse { IsFollowed = follow };
        }

        /// <summary>
        /// Follow a thread automatically (author on create, commenter on first comment when enabled); an earlier
        /// explicit unfollow is respected.
        /// </summary>
        /// <param name="threadId">Thread.</param>
        /// <param name="userId">Member.</param>
        /// <param name="source">Author or Comment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the member follows the thread afterwards.</returns>
        public async Task<bool> AutoFollowAsync(string threadId, string userId, FollowSourceEnum source, CancellationToken token = default)
        {
            bool following = await _Services.Database.Threads.FollowAsync(new ThreadFollow { Id = IdGenerator.Generate(ForumIdPrefixes.ThreadFollow), ThreadId = threadId, UserId = userId, Source = source, CreatedUtc = DateTime.UtcNow },
                _Services.Settings.Forums.MaxFollowsPerMember, token).ConfigureAwait(false);
            if (following) ForumTelemetry.Count(ForumTelemetry.Follows, "target_type", "thread", "action", "auto");
            return following;
        }

        /// <summary>
        /// POST /api/v1/me/follows/enumerate: the caller's forum and thread follows.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of follows.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<EnumerationResult<FollowItemView>> MyFollowsAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            _Services.Forums.Access.RequireEnabled();
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string? rawType = q.FilterString("targetType");
            TargetTypeEnum? type = null;
            if (rawType != null)
            {
                if (ModerationService.TryParseEnum(rawType, out TargetTypeEnum parsed) && (parsed == TargetTypeEnum.Forum || parsed == TargetTypeEnum.Thread)) type = parsed;
                else errors["filters.targetType"] = "Use Forum or Thread.";
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ForumCollections.Follows, false, "follows|" + userId);
            EnumerationResult<FollowRow> page = await _Services.Database.Threads.EnumerateFollowsAsync(userId, request, type, token).ConfigureAwait(false);
            List<string> forumIds = page.Objects.Where(r => r.TargetType == TargetTypeEnum.Forum).Select(r => r.TargetId).ToList();
            List<string> threadIds = page.Objects.Where(r => r.TargetType == TargetTypeEnum.Thread).Select(r => r.TargetId).ToList();
            List<Forum> forums = (await _Services.Database.Forums.ReadManyAsync(forumIds, token).ConfigureAwait(false)).Where(f => ForumAccessService.CanReadForum(f, actor)).ToList();
            Dictionary<string, ForumView> forumViews = (await _Services.Forums.Views.ForumsAsync<ForumView>(forums, actor, token).ConfigureAwait(false)).ToDictionary(f => f.Id, StringComparer.Ordinal);
            List<ThreadContext> contexts = (await _Services.Forums.Access.ContextsAsync(await _Services.Database.Threads.ReadManyAsync(threadIds, token).ConfigureAwait(false), token).ConfigureAwait(false))
                .Where(c => ForumAccessService.CanSeeThread(c, actor)).ToList();
            Dictionary<string, ThreadSummaryView> threadViews = (await _Services.Forums.Views.SummariesAsync(contexts, actor, false, token).ConfigureAwait(false)).ToDictionary(t => t.Id, StringComparer.Ordinal);
            Dictionary<string, ObjectMetadata> forumMeta = await _Services.Metadata.LoadAsync(TargetTypeEnum.ForumFollow, page.Objects.Where(r => r.TargetType == TargetTypeEnum.Forum).Select(r => r.Id), false, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> threadMeta = await _Services.Metadata.LoadAsync(TargetTypeEnum.ThreadFollow, page.Objects.Where(r => r.TargetType == TargetTypeEnum.Thread).Select(r => r.Id), false, token).ConfigureAwait(false);
            List<FollowItemView> items = new List<FollowItemView>();
            foreach (FollowRow row in page.Objects)
            {
                object? target = row.TargetType == TargetTypeEnum.Forum
                    ? (forumViews.TryGetValue(row.TargetId, out ForumView? f) ? f : null)
                    : (threadViews.TryGetValue(row.TargetId, out ThreadSummaryView? t) ? t : null);
                if (target == null) continue;
                ObjectMetadata meta = (row.TargetType == TargetTypeEnum.Forum ? forumMeta : threadMeta).TryGetValue(row.Id, out ObjectMetadata? m) ? m : new ObjectMetadata();
                items.Add(new FollowItemView { Id = row.Id, TargetType = row.TargetType.ToString(), Target = target, CreatedUtc = row.CreatedUtc, Labels = meta.Labels, Tags = meta.Tags });
            }
            return page.With(items);
        }

        #endregion

        #region Publishing

        /// <summary>
        /// Publish a Processing thread once every attachment is Ready: publish time, initial score, audit, mentions, the
        /// NewThreadInForum fan-out, public media, and the search index.
        /// </summary>
        /// <param name="threadId">Thread.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when published now.</returns>
        public async Task<bool> TryPublishAsync(string threadId, CancellationToken token = default)
        {
            ThreadContext? context = await _Services.Forums.Access.LoadAsync(threadId, token).ConfigureAwait(false);
            if (context == null) return false;
            ForumThread thread = context.Thread;
            if (thread.PublishedUtc != null || thread.DeletedUtc != null || thread.ModerationState != ModerationStateEnum.None) return false;
            List<MediaAttachment> media = await _Services.Database.Media.ListByTargetsAsync(TargetTypeEnum.Thread, new[] { thread.Id }, token).ConfigureAwait(false);
            if (media.Any(m => m.Status != MediaStatusEnum.Ready)) return false;
            using (Activity? activity = TrellisKitTelemetry.StartSpan("forums.publish"))
            {
                activity?.SetTag("thread.id", thread.Id);
                activity?.SetTag("forum.id", thread.ForumId);
                activity?.SetTag("thread.type", thread.Type.ToString());
                DateTime now = DateTime.UtcNow;
                double score = _Services.Forums.Trending.Score(0, 0, 0, 0);
                if (!await _Services.Database.Threads.TryPublishAsync(thread.Id, now, score, now, token).ConfigureAwait(false)) return false;
                thread.PublishedUtc = now;
                await _Services.Audit.WriteAsync(null, null, "thread.publish", TargetTypeEnum.Thread.ToString(), thread.Id, Summary(thread), token).ConfigureAwait(false);
                await _Services.Forums.Notifications.ThreadPublishedAsync(context, token).ConfigureAwait(false);
                await _Services.Forums.Promotions.SyncPublicMediaAsync(context, token).ConfigureAwait(false);
                await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Thread, thread.Id, token).ConfigureAwait(false);
                ForumTelemetry.Count(ForumTelemetry.Threads, "action", "publish", "type", thread.Type.ToString(), "outcome", "success");
                activity?.SetStatus(ActivityStatusCode.Ok);
                return true;
            }
        }

        /// <summary>
        /// Publish Processing threads whose media became Ready (the publish worker's pass).
        /// </summary>
        /// <param name="limit">Most threads per pass.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Threads published.</returns>
        public async Task<int> PublishDueAsync(int limit, CancellationToken token = default)
        {
            int published = 0;
            foreach (ForumThread thread in await _Services.Database.Threads.ListPublishCandidatesAsync(Math.Max(1, limit), token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                if (await TryPublishAsync(thread.Id, token).ConfigureAwait(false)) published++;
            }
            return published;
        }

        /// <summary>
        /// Refresh the open review requests gauge.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task RefreshReviewGaugeAsync(CancellationToken token = default)
        {
            ThreadActivityCounts counts = await _Services.Database.Threads.ActivityCountsAsync(DateTime.UtcNow.AddDays(-1), token).ConfigureAwait(false);
            ForumTelemetry.SetOpenReviewRequests(counts.OpenReviewRequests);
        }

        #endregion

        /// <summary>
        /// Plain-text summary of a thread for audit records.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <returns>At most 120 characters.</returns>
        public static string Summary(ForumThread thread)
        {
            ArgumentNullException.ThrowIfNull(thread);
            return thread.Title.Length > 120 ? thread.Title.Substring(0, 120) : thread.Title;
        }

        #endregion

        #region Private-Methods

        private async Task<ThreadView> CreateCoreAsync(ContentActor actor, string userId, RequestInfo info, string forumId, ThreadRequest request, CancellationToken token)
        {
            Forum forum = await _Services.Forums.Access.RequireForumAsync(actor, forumId, token).ConfigureAwait(false);
            if (forum.IsArchived) throw new ApiErrorException(409, "ForumArchived", "This forum is archived and read-only.");
            actor.RequireCanPost();
            if (!ForumAccessService.CreationAllows(forum, actor)) throw new ApiErrorException(403, "ThreadCreationRestricted", "New threads in this forum are limited to " + forum.ThreadCreation.ToString().ToLowerInvariant() + ".");
            await _Services.Forums.Access.RequireNotForumMutedAsync(actor, forum.Id, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            CheckThreadLimits(actor, now);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (request.Type == null) throw ApiErrorException.Field("type", "Use Discussion, Question, Poll, or Feedback.");
            ThreadTypeEnum type = request.Type.Value;
            if (!forum.AllowedThreadTypes.Contains(type)) throw new ApiErrorException(400, "ThreadTypeNotAllowed", "This forum does not allow " + type + " threads.");
            string title = NormalizeTitle(request.Title, errors);
            string body = FeedService.NormalizeBody(request.Body, _Services.Settings.Forums.MaxBodyLength, errors);
            bool bodyRequired = type == ThreadTypeEnum.Discussion || type == ThreadTypeEnum.Question;
            if (bodyRequired && body.Length == 0) errors.TryAdd("body", "Required for " + type + " threads.");
            ContentAudienceEnum audience = ResolveAudience(request.Audience, forum, errors);
            string? focus = NormalizeFocus(request.Feedback?.Focus, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            Poll? poll = null;
            string id = IdGenerator.Generate(ForumIdPrefixes.Thread);
            if (type == ThreadTypeEnum.Poll)
            {
                if (request.Poll == null) throw new ApiErrorException(400, "PollInvalid", "A Poll thread needs a poll.", new Dictionary<string, string> { { "poll", "Required." } });
                poll = _Services.Forums.Polls.Build(request.Poll, id, now);
            }
            else if (request.Poll != null) throw new ApiErrorException(400, "PollInvalid", "Only Poll threads carry a poll.", new Dictionary<string, string> { { "poll", "Only for Poll threads." } });
            if (type != ThreadTypeEnum.Feedback && request.Feedback != null && !String.IsNullOrWhiteSpace(request.Feedback.Focus)) throw ApiErrorException.Field("feedback", "Only Feedback threads carry a focus.");
            List<string> mediaIds = (request.MediaIds ?? new List<string>()).ToList();
            List<MediaAttachment> media = await ValidateMediaAsync(actor, forum, mediaIds, null, token).ConfigureAwait(false);
            if (type == ThreadTypeEnum.Feedback)
            {
                actor.RequireCanUploadVideo();
                if (!media.Any(m => m.Kind == MediaKindEnum.Video)) throw new ApiErrorException(400, "FeedbackVideoRequired", "Attach a video to request feedback.");
                await EnforceFeedbackLimitAsync(forum, userId, token).ConfigureAwait(false);
            }
            WordFilterResult filter = await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.Thread, FilterFields(title, body, poll, focus, media), token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.Thread), token, TargetTypeEnum.Thread).ConfigureAwait(false);
            ConsumeThreadLimits(actor, now);

            if (media.Count > 0) await _Services.Media.AttachAsync(TargetTypeEnum.Thread, id, media.Select(m => m.Id).ToList(), token).ConfigureAwait(false);
            bool showPreview = request.ShowLinkPreview ?? true;
            ForumThread thread = new ForumThread
            {
                Id = id,
                ForumId = forum.Id,
                Kind = ThreadKindEnum.Original,
                Type = type,
                AuthorUserId = userId,
                Title = title,
                Body = body,
                Excerpt = MarkdownNormalizer.Excerpt(body.Length == 0 ? title : body),
                LinkPreviewId = await _Services.LinkPreviews.ResolveForBodyAsync(body, showPreview, token).ConfigureAwait(false),
                ShowLinkPreview = showPreview,
                Audience = audience,
                CommentTargetType = TargetTypeEnum.Thread,
                CommentTargetId = id,
                FeedbackStatus = type == ThreadTypeEnum.Feedback ? FeedbackStatusEnum.Open : null,
                FeedbackFocus = type == ThreadTypeEnum.Feedback ? focus : null,
                FeedbackStatusUtc = type == ThreadTypeEnum.Feedback ? now : null,
                AttachmentCount = media.Count,
                HasVideo = media.Any(m => m.Kind == MediaKindEnum.Video),
                LastActivityUtc = now,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            ThreadFollow follow = new ThreadFollow { Id = IdGenerator.Generate(ForumIdPrefixes.ThreadFollow), ThreadId = id, UserId = userId, Source = FollowSourceEnum.Author, CreatedUtc = now };
            await _Services.Database.Threads.CreateAsync(thread, poll, follow, token).ConfigureAwait(false);
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Thread, id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.WordFilter.RecordFlagsAsync(filter, TargetTypeEnum.Thread, id, userId, token).ConfigureAwait(false);
            await _Services.Mentions.StoreAsync(body, TargetTypeEnum.Thread, id, userId, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "thread.create", TargetTypeEnum.Thread.ToString(), id, Summary(thread), token).ConfigureAwait(false);
            if (type == ThreadTypeEnum.Feedback) await RefreshReviewGaugeAsync(token).ConfigureAwait(false);
            await TryPublishAsync(id, token).ConfigureAwait(false);
            return await ReloadAsync(id, actor, token).ConfigureAwait(false);
        }

        private async Task<ThreadView> UpdateCoreAsync(ContentActor actor, RequestInfo info, string threadId, ThreadRequest request, HashSet<string> present, CancellationToken token)
        {
            ThreadContext context = await _Services.Forums.Access.RequireThreadAsync(actor, threadId, token).ConfigureAwait(false);
            ForumThread thread = context.Thread;
            bool isAuthor = IsAuthor(actor, thread);
            bool authorIsAdmin = await _Services.Forums.Access.IsSystemAdminAsync(thread.AuthorUserId, token).ConfigureAwait(false);
            bool moderator = ForumAccessService.CanModerate(actor, thread.ForumId, authorIsAdmin);
            bool linked = thread.Kind == ThreadKindEnum.LinkedFeedPost;
            bool authorEdit = isAuthor && !linked;
            if (!authorEdit && !moderator && !actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Only the author or a moderator in scope can change this thread.");
            HashSet<string> metadataFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "title", "labels", "tags", "type" };
            if (!authorEdit && !actor.IsSystemAdmin)
            {
                List<string> denied = present.Where(p => !metadataFields.Contains(p)).ToList();
                if (denied.Count > 0) throw ApiErrorException.Forbidden("Moderators may change only the title, labels, tags, and type (Discussion or Question): " + String.Join(", ", denied) + ".");
            }
            if (present.Contains("type") && request.Type != null && request.Type.Value != thread.Type)
            {
                if (!moderator) throw ApiErrorException.Forbidden("Only a moderator in scope can change the thread type.");
                bool swap = (thread.Type == ThreadTypeEnum.Discussion && request.Type.Value == ThreadTypeEnum.Question) || (thread.Type == ThreadTypeEnum.Question && request.Type.Value == ThreadTypeEnum.Discussion);
                if (!swap) throw ApiErrorException.Field("type", "Only Discussion and Question threads can swap types.");
                if (!context.Forum.AllowedThreadTypes.Contains(request.Type.Value)) throw new ApiErrorException(400, "ThreadTypeNotAllowed", "This forum does not allow " + request.Type.Value + " threads.");
            }
            if (authorEdit)
            {
                actor.RequireCanPost();
                _Services.Limits.EnforceMember(actor, FeedService.EditsBucket, _Services.Settings.RateLimits.EditsPerHour, RateLimitService.Hour);
            }
            DateTime now = DateTime.UtcNow;
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string title = present.Contains("title") ? NormalizeTitle(request.Title, errors) : thread.Title;
            string? body = thread.Body;
            if (present.Contains("body") && !linked)
            {
                body = FeedService.NormalizeBody(request.Body, _Services.Settings.Forums.MaxBodyLength, errors);
                if ((thread.Type == ThreadTypeEnum.Discussion || thread.Type == ThreadTypeEnum.Question) && body.Length == 0) errors.TryAdd("body", "Required for " + thread.Type + " threads.");
            }
            ContentAudienceEnum audience = present.Contains("audience") ? ResolveAudience(request.Audience, context.Forum, errors) : thread.Audience;
            string? focus = thread.FeedbackFocus;
            if (present.Contains("feedback"))
            {
                if (thread.Type != ThreadTypeEnum.Feedback) errors["feedback"] = "Only Feedback threads carry a focus.";
                else focus = NormalizeFocus(request.Feedback?.Focus, errors);
            }
            ThreadTypeEnum type = present.Contains("type") && request.Type != null ? request.Type.Value : thread.Type;
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            List<MediaAttachment> before = await _Services.Database.Media.ListByTargetsAsync(TargetTypeEnum.Thread, new[] { thread.Id }, token).ConfigureAwait(false);
            List<MediaAttachment> after = before;
            if (present.Contains("mediaIds") && !linked)
            {
                after = await ValidateMediaAsync(actor, context.Forum, (request.MediaIds ?? new List<string>()).ToList(), thread.Id, token).ConfigureAwait(false);
                if (thread.Type == ThreadTypeEnum.Feedback && !after.Any(m => m.Kind == MediaKindEnum.Video)) throw new ApiErrorException(400, "FeedbackVideoRequired", "A Feedback thread keeps at least one video.");
            }
            if (present.Contains("poll"))
            {
                if (thread.Type != ThreadTypeEnum.Poll || request.Poll == null) throw new ApiErrorException(400, "PollInvalid", "Only Poll threads carry a poll.", new Dictionary<string, string> { { "poll", "Only for Poll threads." } });
                if (!isAuthor && !actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Only the poll author can change the poll.");
            }
            ContentActor author = isAuthor ? actor : await AuthorActorAsync(thread.AuthorUserId, actor, token).ConfigureAwait(false);
            Poll? currentPoll = present.Contains("poll") ? await _Services.Database.Polls.ReadByThreadAsync(thread.Id, token).ConfigureAwait(false) : null;
            Poll? replacement = present.Contains("poll") && currentPoll != null ? _Services.Forums.Polls.BuildEdit(request.Poll!, currentPoll, now) : null;
            WordFilterResult filter = await _Services.WordFilter.CheckAsync(author, TargetTypeEnum.Thread, FilterFields(title, body, replacement, focus, after), token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.Thread), token, TargetTypeEnum.Thread).ConfigureAwait(false);
            if (replacement != null && currentPoll != null) await _Services.Forums.Polls.ApplyEditAsync(currentPoll, replacement, now, token).ConfigureAwait(false);

            bool mediaChanged = AttachmentSummaryText.Differs(before, after);
            bool bodyChanged = !String.Equals(body ?? String.Empty, thread.Body ?? String.Empty, StringComparison.Ordinal);
            bool titleChanged = !String.Equals(title, thread.Title, StringComparison.Ordinal);
            bool typeChanged = type != thread.Type;
            bool contentChanged = bodyChanged || titleChanged || mediaChanged || typeChanged;
            bool moderatorEdit = !isAuthor;
            bool silent = !moderatorEdit && (thread.PublishedUtc == null || (now - thread.PublishedUtc.Value).TotalSeconds <= _Services.Settings.Content.EditGraceSeconds);
            ThreadRevision? revision = contentChanged && !silent ? new ThreadRevision
            {
                Id = IdGenerator.Generate(ForumIdPrefixes.ThreadRevision),
                ThreadId = thread.Id,
                Title = thread.Title,
                Body = thread.Body,
                Type = thread.Type,
                AttachmentSummary = AttachmentSummaryText.Describe(before, after),
                EditedByUserId = actor.UserId,
                EditedByRole = moderatorEdit ? ThreadEditorRoleEnum.Moderator : ThreadEditorRoleEnum.Author,
                CreatedUtc = now
            } : null;
            if (present.Contains("mediaIds") && mediaChanged) await _Services.Media.AttachAsync(TargetTypeEnum.Thread, thread.Id, after.Select(m => m.Id).ToList(), token).ConfigureAwait(false);
            bool showPreview = present.Contains("showLinkPreview") && !linked ? (request.ShowLinkPreview ?? true) : thread.ShowLinkPreview;
            if (!linked && (bodyChanged || showPreview != thread.ShowLinkPreview)) thread.LinkPreviewId = await _Services.LinkPreviews.ResolveForBodyAsync(body, showPreview, token).ConfigureAwait(false);
            thread.Title = title;
            thread.Body = body;
            thread.Excerpt = linked ? thread.Excerpt : MarkdownNormalizer.Excerpt(String.IsNullOrEmpty(body) ? title : body);
            thread.Type = type;
            thread.ShowLinkPreview = showPreview;
            thread.Audience = audience;
            thread.FeedbackFocus = focus;
            if (revision != null) thread.EditedUtc = now;
            thread.UpdatedUtc = now;
            thread = await _Services.Database.Threads.UpdateAsync(thread, revision, token).ConfigureAwait(false);
            if (revision != null) await _Services.Metadata.SnapshotAsync(TargetTypeEnum.Thread, thread.Id, TargetTypeEnum.ThreadRevision, revision.Id, token).ConfigureAwait(false);
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Thread, thread.Id, present.Contains("labels") ? request.Labels : null, present.Contains("tags") ? request.Tags : null, token).ConfigureAwait(false);
            await _Services.WordFilter.RecordFlagsAsync(filter, TargetTypeEnum.Thread, thread.Id, thread.AuthorUserId, token).ConfigureAwait(false);
            if (bodyChanged && !linked)
            {
                await _Services.Mentions.StoreAsync(body, TargetTypeEnum.Thread, thread.Id, thread.AuthorUserId, token).ConfigureAwait(false);
                if (thread.PublishedUtc != null) await _Services.Forums.Notifications.MentionsAsync(TargetTypeEnum.Thread, thread.Id, thread.AuthorUserId, thread, null, thread.Excerpt, token).ConfigureAwait(false);
            }
            if (moderatorEdit)
            {
                List<string> changed = new List<string>();
                if (titleChanged) changed.Add("title");
                if (typeChanged) changed.Add("type");
                if (present.Contains("labels")) changed.Add("labels");
                if (present.Contains("tags")) changed.Add("tags");
                await _Services.Forums.Moderation.RecordAsync(actor, thread, ModerationActionTypeEnum.EditMetadata, "Edited " + (changed.Count == 0 ? "metadata" : String.Join(", ", changed)), null, null, null, token).ConfigureAwait(false);
            }
            await _Services.Audit.WriteAsync(info, "thread.update", TargetTypeEnum.Thread.ToString(), thread.Id, Summary(thread) + (revision != null ? " (revision)" : String.Empty), token).ConfigureAwait(false);
            ThreadContext updated = (await _Services.Forums.Access.LoadAsync(thread.Id, token).ConfigureAwait(false))!;
            await _Services.Forums.Promotions.SyncPublicMediaAsync(updated, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Thread, thread.Id, token).ConfigureAwait(false);
            if (thread.PublishedUtc == null && mediaChanged) await TryPublishAsync(thread.Id, token).ConfigureAwait(false);
            return await ReloadAsync(thread.Id, actor, token).ConfigureAwait(false);
        }

        private async Task<EnumerationResult<ThreadSummaryView>> ListCoreAsync(ContentActor actor, EnumerationQuery q, EnumerationCollection collection, Forum? forum, string scope, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string? search = String.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim();
            if (search != null && String.IsNullOrWhiteSpace(q.Ordering)) q.Ordering = "Relevance";
            if (search == null && String.Equals(q.Ordering?.Trim(), "Relevance", StringComparison.OrdinalIgnoreCase)) errors["ordering"] = "Relevance needs a search.";
            DateTime now = DateTime.UtcNow;
            ThreadFilter filter = new ThreadFilter
            {
                ForumId = forum?.Id,
                Types = ForumFilters.Enums<ThreadTypeEnum>(q, "type", errors),
                FeedbackStatuses = ForumFilters.Enums<FeedbackStatusEnum>(q, "feedbackStatus", errors),
                Answered = q.FilterBool("answered", errors),
                Unanswered = q.FilterBool("unanswered", errors) ?? false,
                PollOpen = q.FilterBool("pollOpen", errors),
                AuthorUserId = q.FilterString("authorUserId"),
                HasMedia = q.FilterBool("hasMedia", errors) ?? false,
                HasVideo = q.FilterBool("hasVideo", errors) ?? false,
                AudienceFilter = ForumFilters.Enums<ContentAudienceEnum>(q, "audience", errors),
                NowUtc = now
            };
            if (forum == null) filter.ForumIds = ForumIdsFilter(q, errors);
            bool includePinned = q.FilterBool("includePinned", errors) ?? true;
            bool includeComments = q.FilterBool("includeComments", errors) ?? true;
            if (q.FilterBool("followedOnly", errors) == true) filter.FollowedByUserId = actor.RequireUserId();
            if (q.FilterBool("unreadOnly", errors) == true)
            {
                filter.UnreadForUserId = actor.RequireUserId();
                filter.UnreadWindowStartUtc = now.AddDays(-_Services.Settings.Forums.UnreadWindowDays);
            }
            List<ThreadStateEnum> states = ForumFilters.Enums<ThreadStateEnum>(q, "state", errors);
            bool own = !String.IsNullOrEmpty(actor.UserId) && String.Equals(filter.AuthorUserId, actor.UserId, StringComparison.Ordinal);
            if (states.Contains(ThreadStateEnum.Deleted)) errors["filters.state"] = "Use Processing, Published, Hidden, or Removed.";
            else if (states.Count > 0 && !own && !actor.IsSystemAdmin && states.Any(s => s != ThreadStateEnum.Published)) errors["filters.state"] = "Only your own threads can be listed in other states (authorUserId is you).";
            else if (states.Count > 0)
            {
                filter.States = states;
                if (!actor.IsSystemAdmin) filter.StateOwnerUserId = actor.UserId;
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (!actor.IsSystemAdmin)
            {
                filter.Audiences = ContentAudienceRule.VisibleAudiences(actor);
                if (forum == null) filter.ForumIds = await ReadableForumIdsAsync(actor, filter.ForumIds, token).ConfigureAwait(false);
            }
            if (!String.IsNullOrEmpty(actor.UserId)) filter.HiddenAuthorIds = (await _Services.Blocks.HiddenAuthorsForAsync(actor.UserId, token).ConfigureAwait(false)).ToList();
            EnumerationRequest request = _Services.Enumeration.Prepare(q, collection, actor.IsStaffFor(TargetTypeEnum.Thread), scope + "|" + (actor.UserId ?? "-"));
            if (!actor.IsSystemAdmin && forum == null && filter.ForumIds.Count == 0)
                return new EnumerationResult<ThreadSummaryView> { MaxResults = request.MaxResults, Skip = request.Skip, TotalRecords = request.CountTotals ? 0 : null, RecordsRemaining = request.CountTotals ? 0 : null };
            ThreadSearchCandidates? candidates = null;
            if (search != null)
            {
                _Services.Limits.EnforceMember(actor, "forums.search", _Services.Settings.Forums.SearchPerMinute, RateLimitService.Minute);
                candidates = await _Services.Forums.Search.ThreadCandidatesAsync(search, includeComments, forum?.Id, token).ConfigureAwait(false);
                filter.CandidateIds = candidates.ThreadIds;
                request.Query.Search = null;
            }
            bool pinnedFirst = forum != null && includePinned && search == null && (request.Ordering.Name == "Active" || request.Ordering.Name == "Hot");
            filter.PinnedFirst = pinnedFirst;
            EnumerationResult<ForumThread> page = await _Services.Database.Threads.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            List<ForumThread> threads = page.Objects;
            List<ThreadContext> contexts = await _Services.Forums.Access.ContextsAsync(threads, token).ConfigureAwait(false);
            List<ThreadSummaryView> views = await _Services.Forums.Views.SummariesAsync(contexts, actor, false, token).ConfigureAwait(false);
            if (candidates != null) await _Services.Forums.Search.ApplySnippetsAsync(views, contexts, search!, candidates, includeComments, token).ConfigureAwait(false);
            return page.With(views);
        }

        private async Task<List<string>> ReadableForumIdsAsync(ContentActor actor, List<string> requested, CancellationToken token)
        {
            EnumerationRequest request = _Services.Enumeration.Prepare(new EnumerationQuery { MaxResults = 1000 }, ForumCollections.AdminForums, true, "readable-forums", 1000);
            ForumFilter filter = new ForumFilter { IncludeArchived = true };
            EnumerationResult<Forum> forums = await _Services.Database.Forums.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            List<string> readable = forums.Objects.Where(f => ForumAccessService.CanReadForum(f, actor)).Select(f => f.Id).ToList();
            if (requested.Count == 0) return readable;
            HashSet<string> set = new HashSet<string>(readable, StringComparer.Ordinal);
            List<string> narrowed = requested.Where(set.Contains).ToList();
            return narrowed.Count == 0 ? new List<string> { "frm_none" } : narrowed;
        }

        private async Task<List<string>> PublicForumIdsAsync(CancellationToken token)
        {
            EnumerationRequest request = _Services.Enumeration.Prepare(new EnumerationQuery { MaxResults = 1000 }, ForumCollections.AdminForums, true, "public-forums", 1000);
            EnumerationResult<Forum> forums = await _Services.Database.Forums.EnumerateAsync(request, new ForumFilter { IncludeArchived = true, Audience = ContentAudienceEnum.Public }, token).ConfigureAwait(false);
            return forums.Objects.Select(f => f.Id).ToList();
        }

        private List<string> ForumIdsFilter(EnumerationQuery q, Dictionary<string, string> errors)
        {
            List<string> ids = ForumFilters.List(q, "forumIds") ?? new List<string>();
            if (ids.Count > MaxForumIds) errors["filters.forumIds"] = "At most " + MaxForumIds + " forums.";
            return ids;
        }

        private async Task<ThreadContext> RequireModeratedAsync(ContentActor actor, string threadId, CancellationToken token)
        {
            ThreadContext context = await _Services.Forums.Access.RequireThreadAsync(actor, threadId, token).ConfigureAwait(false);
            bool authorIsAdmin = await _Services.Forums.Access.IsSystemAdminAsync(context.Thread.AuthorUserId, token).ConfigureAwait(false);
            if (!ForumAccessService.CanModerate(actor, context.Thread.ForumId, authorIsAdmin))
                throw ApiErrorException.Forbidden(actor.CanModerateForum(context.Thread.ForumId) ? "Moderators cannot act on content by a SystemAdmin." : "A moderator role for this forum is required.");
            return context;
        }

        private async Task<List<MediaAttachment>> ValidateMediaAsync(ContentActor actor, Forum forum, List<string> mediaIds, string? threadId, CancellationToken token)
        {
            if (mediaIds.Count > 0 && !forum.AllowMedia && !actor.IsSystemAdmin) throw new ApiErrorException(403, "MediaNotAllowed", "This forum does not allow media.");
            List<MediaAttachment> media = await _Services.Media.ValidateAttachableAsync(actor, mediaIds, _Services.Settings.Forums.MaxAttachments, _Services.Settings.Forums.MaxVideosPerThread,
                threadId == null ? null : TargetTypeEnum.Thread, threadId, token).ConfigureAwait(false);
            if (media.Any(m => m.Kind == MediaKindEnum.Video) && !forum.AllowVideo && !actor.IsSystemAdmin) throw new ApiErrorException(403, "VideoUploadNotAllowed", "This forum does not allow video.");
            return media;
        }

        private async Task EnforceFeedbackLimitAsync(Forum forum, string authorUserId, CancellationToken token)
        {
            int limit = forum.MaxOpenFeedbackPerMember ?? _Services.Settings.Forums.MaxOpenFeedbackPerMember;
            if (limit <= 0) return;
            long open = await _Services.Database.Threads.CountOpenFeedbackAsync(authorUserId, forum.MaxOpenFeedbackPerMember == null ? null : forum.Id, token).ConfigureAwait(false);
            if (open >= limit) throw new ApiErrorException(409, "FeedbackLimitReached", "You already have " + open + " open feedback requests (at most " + limit + ").");
        }

        private void CheckThreadLimits(ContentActor actor, DateTime now)
        {
            bool newAccount = actor.User != null && (now - actor.User.CreatedUtc).TotalHours < _Services.Settings.Comments.NewAccountHours;
            int perHour = newAccount ? _Services.Settings.Forums.NewAccountThreadsPerHour : _Services.Settings.Forums.ThreadsPerHour;
            int perDay = newAccount ? _Services.Settings.Forums.NewAccountThreadsPerDay : _Services.Settings.Forums.ThreadsPerDay;
            _Services.Limits.CheckMember(actor, "forums.threads.hour", perHour, RateLimitService.Hour);
            _Services.Limits.CheckMember(actor, "forums.threads.day", perDay, RateLimitService.Day);
        }

        private void ConsumeThreadLimits(ContentActor actor, DateTime now)
        {
            // Only a thread that passed validation counts: a rejected attempt never spends the member's hourly allowance.
            bool newAccount = actor.User != null && (now - actor.User.CreatedUtc).TotalHours < _Services.Settings.Comments.NewAccountHours;
            int perHour = newAccount ? _Services.Settings.Forums.NewAccountThreadsPerHour : _Services.Settings.Forums.ThreadsPerHour;
            int perDay = newAccount ? _Services.Settings.Forums.NewAccountThreadsPerDay : _Services.Settings.Forums.ThreadsPerDay;
            _Services.Limits.EnforceMember(actor, "forums.threads.hour", perHour, RateLimitService.Hour);
            _Services.Limits.EnforceMember(actor, "forums.threads.day", perDay, RateLimitService.Day);
        }

        private async Task<ContentActor> AuthorActorAsync(string authorUserId, ContentActor fallback, CancellationToken token)
        {
            try
            {
                return await _Services.Actors.ForUserAsync(authorUserId, token).ConfigureAwait(false);
            }
            catch (ApiErrorException)
            {
                return fallback;
            }
        }

        private void RequirePublic(string? sourceIp)
        {
            _Services.Forums.Access.RequireEnabled();
            if (!_Services.Property.Property.Features.PublicSamples) throw ApiErrorException.NotFound("Public threads");
            _Services.Limits.EnforceIp(sourceIp, "public.threads", _Services.Settings.RateLimits.PublicFeedPerMinute, RateLimitService.Minute);
        }

        private static ContentAudienceEnum ResolveAudience(ContentAudienceEnum? requested, Forum forum, Dictionary<string, string> errors)
        {
            if (requested == null) return forum.Audience;
            if (ForumAccessService.Rank(requested.Value) < ForumAccessService.Rank(forum.Audience))
            {
                errors["audience"] = "A thread cannot be broader than its forum (" + forum.Audience + ").";
                return forum.Audience;
            }
            return requested.Value;
        }

        private static string NormalizeTitle(string? title, Dictionary<string, string> errors)
        {
            if (title == null || !MarkdownNormalizer.IsWellFormed(title))
            {
                errors["title"] = "Use " + MinTitleLength + " to " + MaxTitleLength + " characters.";
                return String.Empty;
            }
            string normalized = MarkdownNormalizer.NormalizeSingleLine(title);
            int length = MarkdownNormalizer.Length(normalized);
            if (length < MinTitleLength || length > MaxTitleLength) errors["title"] = "Use " + MinTitleLength + " to " + MaxTitleLength + " characters.";
            return normalized;
        }

        private static string? NormalizeFocus(string? focus, Dictionary<string, string> errors)
        {
            if (focus == null) return null;
            if (!MarkdownNormalizer.IsWellFormed(focus))
            {
                errors["feedback.focus"] = "Must be valid UTF-8 text.";
                return null;
            }
            string normalized = MarkdownNormalizer.Normalize(focus);
            if (MarkdownNormalizer.Length(normalized) > MaxFocusLength) errors["feedback.focus"] = "At most " + MaxFocusLength + " characters.";
            return normalized.Length == 0 ? null : normalized;
        }

        private static Dictionary<string, string?> FilterFields(string title, string? body, Poll? poll, string? focus, IReadOnlyList<MediaAttachment> media)
        {
            Dictionary<string, string?> fields = FeedService.FilterFields(title, body, media);
            if (poll != null)
                for (int i = 0; i < poll.Options.Count; i++) fields["poll.options[" + i + "].label"] = poll.Options[i].Label;
            if (focus != null) fields["feedback.focus"] = focus;
            return fields;
        }

        private static bool IsAuthor(ContentActor actor, ForumThread thread)
        {
            return actor.UserId != null && String.Equals(thread.AuthorUserId, actor.UserId, StringComparison.Ordinal);
        }

        private static string ScopeKey(ContentActor actor)
        {
            List<string>? scope = actor.ForumScope;
            return scope == null ? "*" : String.Join(",", scope);
        }

        #endregion
    }
}
