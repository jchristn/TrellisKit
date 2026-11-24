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
    /// Feed posts (contract section 5 and 10.1): the member feed with its pinned prefix, read, create, edit with
    /// revision history, scheduling and publishing (held while attachments process), soft delete, pin and unpin, the
    /// caller's posts, the anonymous public feed (gated by the property's features.publicSamples), the admin feed and
    /// engagement metrics, and the moderation hooks the moderation feature calls. Thread safety: safe to call
    /// concurrently; publishing is a conditional update, so a post publishes exactly once.
    /// </summary>
    public class FeedService
    {
        #region Public-Members

        /// <summary>
        /// Rate limit bucket of post creation (feed.postsPerHour; member and admin routes).
        /// </summary>
        public const string PostsBucket = "feed.posts";

        /// <summary>
        /// Rate limit bucket of post and comment edits (rateLimits.editsPerHour).
        /// </summary>
        public const string EditsBucket = "content.edits";

        /// <summary>
        /// Per-IP rate limit bucket of the anonymous public feed (rateLimits.publicFeedPerMinute).
        /// </summary>
        public const string PublicBucket = "public.feed";

        /// <summary>
        /// Longest title.
        /// </summary>
        public const int MaxTitleLength = 140;

        /// <summary>
        /// Called after a write that may make a post publishable (media became Ready). The composition root wires it
        /// to the publish worker's Signal.
        /// </summary>
        public Action? PublishSignal { get; set; } = null;

        /// <summary>
        /// Optional source of the moderation actions preview in the admin detail (at most 5 items; the moderation
        /// feature sets it). When null the preview carries the count from the moderationactions table only.
        /// </summary>
        public Func<string, CancellationToken, Task<BoundedPreview<object>>>? ModerationActionsPreview { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private int _Attached = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="services">Application services (read on each call).</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public FeedService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Wire the media hooks (once): attached media visibility follows the content, alt text passes the word filter,
        /// admin media deletes recompute attachmentcount and hasvideo, and media becoming Ready or Failed signals the
        /// publish worker (and refreshes public copies of Public posts).
        /// </summary>
        public void Attach()
        {
            if (Interlocked.Exchange(ref _Attached, 1) != 0) return;
            _Services.Media.AttachedVisibility = (media, actor, token) => _Services.ContentAccess.CanSeeAttachedMediaAsync(media, actor, token);
            _Services.Media.AltTextFilter = async (actor, text, token) =>
            {
                await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.Media, new Dictionary<string, string?> { { "altText", text } }, token).ConfigureAwait(false);
            };
            _Services.Media.AddDetachedHandler(OnMediaDetachedAsync);
            _Services.Media.AddSettledHandler(OnMediaSettledAsync);
        }

        #region Member-Routes

        /// <summary>
        /// GET /api/v1/feed: Published posts (ContentCollections.Feed). The Feed ordering places pinned posts first (pinnedUtc descending),
        /// then unpinned posts; all objects count toward maxResults and share one keyset. Posts by authors the caller blocked (minus staff) are
        /// omitted.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 429 RateLimited.</exception>
        public async Task<EnumerationResult<FeedPostView>> ListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (q.MaxResults == null) q.MaxResults = 20;
            Dictionary<string, string> errors = new Dictionary<string, string>();
            ContentAudienceEnum? audience = q.FilterEnum<ContentAudienceEnum>("audience", errors);
            FeedPostFilter filter = new FeedPostFilter
            {
                PublishedAfterUtc = q.FilterUtc("publishedAfterUtc", errors),
                PublishedBeforeUtc = q.FilterUtc("publishedBeforeUtc", errors),
                Audiences = ListedAudiences(actor, audience)
            };
            CheckWindow(filter.PublishedAfterUtc, filter.PublishedBeforeUtc, "publishedAfterUtc", "publishedBeforeUtc", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ContentCollections.Feed, actor.IsStaffFor(TargetTypeEnum.FeedPost), "feed", 50);
            if (!String.IsNullOrEmpty(actor.UserId)) filter.HiddenAuthorIds = (await _Services.Blocks.HiddenAuthorsForAsync(actor.UserId, token).ConfigureAwait(false)).ToList();
            EnumerationResult<FeedPost> page = await _Services.Database.FeedPosts.EnumerateFeedAsync(request, filter, token).ConfigureAwait(false);
            List<FeedPost> posts = page.Objects;
            // Promoted cards whose thread the caller cannot read are skipped, so promotion never widens an audience
            // (docs/PHASE3_CONTRACT.md section 8 and docs/ACCESS_MODELS_CONTRACT.md section 2: gated forums hide them).
            List<FeedPost> visible = new List<FeedPost>();
            foreach (FeedPost post in posts)
            {
                if (_Services.ContentAccess.PostVisible == null || await _Services.ContentAccess.PostVisible(post, actor, token).ConfigureAwait(false)) visible.Add(post);
            }
            return page.With(await _Services.ContentViews.FeedPostsAsync(visible, actor, false, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/feed/{id}.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="postId">Post identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Post.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotFound, 429 RateLimited.</exception>
        public async Task<FeedPostView> GetAsync(ContentActor actor, string postId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            FeedPost post = await _Services.ContentAccess.RequireVisiblePostAsync(actor, postId, token).ConfigureAwait(false);
            return await ViewAsync(post, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/feed: create a post (SystemAdmin, or canPostToFeed and canPost).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The post (Scheduled, Processing, or Published).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, MediaLimitExceeded, or ContentFiltered; 403 Forbidden, PostingNotAllowed,
        /// MediaNotAllowed, VideoUploadNotAllowed, or Muted; 409 MediaNotAttachable or PinLimitReached; 429 RateLimited.</exception>
        public async Task<FeedPostView> CreateAsync(ContentActor actor, RequestInfo info, FeedPostRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            actor.RequireUserId();
            if (!actor.IsSystemAdmin && !actor.CanPostToFeed) throw ApiErrorException.Forbidden("Posting to the feed has not been enabled for your account.");
            actor.RequireCanPost();
            FeedPostView created = await CreateCoreAsync(actor, actor, info, request, token).ConfigureAwait(false);
            await new Metrics.ProductMetricEventService(_Services).ObserveAsync("member.firstAction", actor.UserId, token).ConfigureAwait(false);
            return created;
        }

        /// <summary>
        /// PUT /api/v1/feed/{id} (and the admin route): edit a post. Authors (who still have canPost) edit their own
        /// posts in every state; SystemAdmin edits any post. Edits within content.editGraceSeconds of publishing (and
        /// edits before publishing) are silent; later edits that change the title, body, or attachments store a revision
        /// and set editedUtc.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="postId">Post identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Top-level fields present in the body (null values clear them).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The post.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, MediaLimitExceeded, or ContentFiltered; 403 Forbidden, PostingNotAllowed,
        /// MediaNotAllowed, VideoUploadNotAllowed, or Muted; 404 NotFound; 409 MediaNotAttachable; 429 RateLimited.</exception>
        public async Task<FeedPostView> UpdateAsync(ContentActor actor, RequestInfo info, string postId, FeedPostRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            using (Activity? activity = TrellisKitTelemetry.StartSpan("feed.update"))
            {
                try
                {
                    FeedPostView view = await UpdateCoreAsync(actor, info, postId, request, present, token).ConfigureAwait(false);
                    Count("update", "success");
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return view;
                }
                catch (ApiErrorException)
                {
                    Count("update", "rejected");
                    throw;
                }
            }
        }

        /// <summary>
        /// DELETE /api/v1/feed/{id} (author or SystemAdmin): soft delete. The post leaves every list, its comments
        /// become unreachable, its pin and public media copies are removed, and its labels and tags are deleted. Media
        /// stays attached for moderation.retainRemovedDays (the moderation feature's purge worker deletes it).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="postId">Post identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task DeleteAsync(ContentActor actor, RequestInfo info, string postId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            FeedPost post = await RequireOwnedAsync(actor, postId, token).ConfigureAwait(false);
            await DeleteCoreAsync(post, actor.UserId, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "feedPost.delete", TargetTypeEnum.FeedPost.ToString(), post.Id, Summary(post), token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/feed/{id}/pin and /unpin (SystemAdmin). Only Published posts can be pinned; more than
        /// feed.maxPinned is 409 PinLimitReached. Idempotent.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="postId">Post identifier.</param>
        /// <param name="pin">Pin (true) or unpin (false).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The post.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound, 409 NotPublished or PinLimitReached.</exception>
        public async Task<FeedPostView> SetPinnedAsync(ContentActor actor, RequestInfo info, string postId, bool pin, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Only a SystemAdmin can pin or unpin posts.");
            string action = pin ? "pin" : "unpin";
            using (Activity? activity = TrellisKitTelemetry.StartSpan("feed." + action))
            {
                FeedPost post = await _Services.ContentAccess.RequireVisiblePostAsync(actor, postId, token).ConfigureAwait(false);
                DateTime now = DateTime.UtcNow;
                if (pin)
                {
                    if (post.StateAt(now) != FeedPostStateEnum.Published)
                    {
                        Count(action, "rejected");
                        throw new ApiErrorException(409, "NotPublished", "Only published posts can be pinned.");
                    }
                    if (!await _Services.Database.FeedPosts.PinAsync(post.Id, actor.UserId, now, _Services.Settings.Feed.MaxPinned, token).ConfigureAwait(false))
                    {
                        Count(action, "rejected");
                        throw new ApiErrorException(409, "PinLimitReached", "At most " + _Services.Settings.Feed.MaxPinned + " posts can be pinned. Unpin one first.");
                    }
                }
                else
                {
                    await _Services.Database.FeedPosts.UnpinAsync(post.Id, now, token).ConfigureAwait(false);
                }
                Count(action, "success");
                await _Services.Audit.WriteAsync(info, "feedPost." + action, TargetTypeEnum.FeedPost.ToString(), post.Id, Summary(post), token).ConfigureAwait(false);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return await ViewAsync(await RequirePostAsync(post.Id, token).ConfigureAwait(false), actor, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// GET /api/v1/feed/{id}/revisions: the post's revisions, newest first (default 20, max 50), visible to everyone
        /// who can see the post (SystemAdmin also for deleted posts).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="postId">Post identifier.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotFound, 429 RateLimited.</exception>
        public async Task<EnumerationResult<FeedPostRevisionView>> ListRevisionsAsync(ContentActor actor, string postId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            FeedPost post = actor.IsSystemAdmin ? await RequirePostAsync(postId, token).ConfigureAwait(false) : await _Services.ContentAccess.RequireVisiblePostAsync(actor, postId, token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (q.MaxResults == null) q.MaxResults = 20;
            bool staff = actor.IsStaffFor(TargetTypeEnum.FeedPostRevision);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ContentCollections.FeedPostRevisions, staff, "revisions|" + post.Id, 50);
            EnumerationResult<FeedPostRevision> page = await _Services.Database.FeedPosts.EnumerateRevisionsAsync(post.Id, request, token).ConfigureAwait(false);
            return page.With(await RevisionViewsAsync(page.Objects, staff, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/me/feed-posts: the caller's posts in every state except Deleted, newest first. Filter state.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden (admin token), 429 RateLimited.</exception>
        public async Task<EnumerationResult<FeedPostView>> ListMineAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            _Services.Media.EnforceRead(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (q.MaxResults == null) q.MaxResults = 20;
            Dictionary<string, string> errors = new Dictionary<string, string>();
            FeedPostStateEnum? state = q.FilterEnum<FeedPostStateEnum>("state", errors);
            if (state == FeedPostStateEnum.Deleted) errors["filters.state"] = "Use Scheduled, Processing, Published, Hidden, or Removed.";
            ContentAudienceEnum? audience = q.FilterEnum<ContentAudienceEnum>("audience", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ContentCollections.MyFeedPosts, actor.IsStaffFor(TargetTypeEnum.FeedPost), "mine|" + userId, 50);
            EnumerationResult<FeedPost> page = await _Services.Database.FeedPosts.EnumerateByAuthorAsync(userId, request, state, audience, DateTime.UtcNow, token).ConfigureAwait(false);
            return page.With(await _Services.ContentViews.FeedPostsAsync(page.Objects, actor, false, token).ConfigureAwait(false));
        }

        #endregion

        #region Public-Feed

        /// <summary>
        /// GET /api/v1/public/feed (anonymous): Published, unmoderated Public posts, newest publishedUtc first (default
        /// 10, max 20). 404 when the property's features.publicSamples is false.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="sourceIp">Caller address (rateLimits.publicFeedPerMinute).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotFound, 429 RateLimited.</exception>
        public async Task<EnumerationResult<PublicFeedPost>> ListPublicAsync(EnumerationQuery? query, string? sourceIp, CancellationToken token = default)
        {
            RequirePublicSamples(sourceIp);
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (q.MaxResults == null) q.MaxResults = 10;
            Dictionary<string, string> errors = new Dictionary<string, string>();
            ContentAudienceEnum? audience = q.FilterEnum<ContentAudienceEnum>("audience", errors);
            FeedPostFilter filter = new FeedPostFilter
            {
                PublicOnly = true,
                PublishedAfterUtc = q.FilterUtc("publishedAfterUtc", errors),
                PublishedBeforeUtc = q.FilterUtc("publishedBeforeUtc", errors),
                Audiences = audience == null || audience == ContentAudienceEnum.Public ? new List<ContentAudienceEnum> { ContentAudienceEnum.Public } : new List<ContentAudienceEnum>()
            };
            CheckWindow(filter.PublishedAfterUtc, filter.PublishedBeforeUtc, "publishedAfterUtc", "publishedBeforeUtc", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ContentCollections.PublicFeed, false, "public", 20);
            EnumerationResult<FeedPost> page = await _Services.Database.FeedPosts.EnumerateFeedAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await _Services.ContentViews.PublicPostsAsync(page.Objects, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/public/feed/{id} (anonymous): one Public, Published, unmoderated post.
        /// </summary>
        /// <param name="postId">Post identifier.</param>
        /// <param name="sourceIp">Caller address.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Post.</returns>
        /// <exception cref="ApiErrorException">404 NotFound, 429 RateLimited.</exception>
        public async Task<PublicFeedPost> GetPublicAsync(string postId, string? sourceIp, CancellationToken token = default)
        {
            RequirePublicSamples(sourceIp);
            FeedPost? post = String.IsNullOrWhiteSpace(postId) ? null : await _Services.Database.FeedPosts.ReadAsync(postId, token).ConfigureAwait(false);
            if (post == null || !IsPublic(post, DateTime.UtcNow)) throw ApiErrorException.NotFound("Feed post");
            return (await _Services.ContentViews.PublicPostsAsync(new[] { post }, token).ConfigureAwait(false))[0];
        }

        #endregion

        #region Admin-Routes

        /// <summary>
        /// GET /api/v1/admin/feed: every post including Deleted (ContentCollections.AdminFeed). The standard
        /// createdAfterUtc and createdBeforeUtc window applies to createdUtc; filters state, authorUserId, pinned,
        /// audience, and the published window publishedAfterUtc and publishedBeforeUtc (aliases fromUtc and toUtc; a
        /// name and its alias with different values is 400).
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with exact totals.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken.</exception>
        public async Task<EnumerationResult<AdminFeedItem>> ListAdminAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            DateTime now = DateTime.UtcNow;
            AdminFeedFilter filter = new AdminFeedFilter
            {
                State = q.FilterEnum<FeedPostStateEnum>("state", errors),
                AuthorUserId = q.FilterString("authorUserId"),
                Pinned = q.FilterBool("pinned", errors),
                FromUtc = AliasUtc(q, "publishedAfterUtc", "fromUtc", errors),
                ToUtc = AliasUtc(q, "publishedBeforeUtc", "toUtc", errors),
                Audience = q.FilterEnum<ContentAudienceEnum>("audience", errors),
                NowUtc = now
            };
            CheckWindow(filter.FromUtc, filter.ToUtc, "publishedAfterUtc", "publishedBeforeUtc", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ContentCollections.AdminFeed, true, "admin-feed");
            EnumerationResult<FeedPost> page = await _Services.Database.FeedPosts.EnumerateAdminAsync(request, filter, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(page.Objects.Select(p => (string?)p.AuthorUserId), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.FeedPost, page.Objects.Select(p => p.Id), true, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(p => new AdminFeedItem
            {
                Id = p.Id,
                Kind = p.Kind,
                Title = p.Title,
                Excerpt = p.Excerpt,
                Author = users.TryGetValue(p.AuthorUserId, out UserSummary? u) ? u : UserSummary.Deleted(),
                State = p.StateAt(now),
                Visibility = p.Visibility,
                Audience = p.Audience,
                IsPinned = p.IsPinned,
                PublishAtUtc = p.PublishAtUtc,
                PublishedUtc = p.PublishedUtc,
                CreatedUtc = p.CreatedUtc,
                EditedUtc = p.EditedUtc,
                DeletedUtc = p.DeletedUtc,
                AttachmentCount = p.AttachmentCount,
                HasVideo = p.HasVideo,
                CommentCount = p.CommentCount,
                ReactionTotal = p.ReactionTotal,
                OpenReportCount = p.OpenReportCount,
                Labels = metadata.TryGetValue(p.Id, out ObjectMetadata? m) ? m.Labels : new List<string>(),
                Tags = m != null ? m.Tags : new Dictionary<string, string>()
            }).ToList());
        }

        /// <summary>
        /// GET /api/v1/admin/feed/{id}: the post in any state (including Deleted) with bounded previews of revisions and
        /// moderation actions (at most 5 each) and its report counts.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="postId">Post identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<AdminFeedPostDetail> GetAdminAsync(ContentActor actor, string postId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            FeedPost post = await RequirePostAsync(postId, token).ConfigureAwait(false);
            AdminFeedPostDetail detail = (await _Services.ContentViews.BuildPostsAsync<AdminFeedPostDetail>(new[] { post }, actor, true, token).ConfigureAwait(false))[0];
            detail.DeletedUtc = post.DeletedUtc;
            detail.DeletedByUserId = post.DeletedByUserId;
            detail.OpenReportCount = post.OpenReportCount;
            EnumerationRequest request = _Services.Enumeration.Prepare(new EnumerationQuery { MaxResults = 5 }, ContentCollections.FeedPostRevisions, true, "revisions|" + post.Id, 5);
            EnumerationResult<FeedPostRevision> revisions = await _Services.Database.FeedPosts.EnumerateRevisionsAsync(post.Id, request, token).ConfigureAwait(false);
            detail.Revisions = new BoundedPreview<FeedPostRevisionView>
            {
                Objects = await RevisionViewsAsync(revisions.Objects, true, token).ConfigureAwait(false),
                TotalRecords = revisions.TotalRecords ?? post.RevisionCount,
                Enumerate = Constants.ApiPrefix + "/feed/" + post.Id + "/revisions/enumerate"
            };
            if (ModerationActionsPreview != null) detail.ModerationActions = await ModerationActionsPreview(post.Id, token).ConfigureAwait(false);
            else
            {
                detail.ModerationActions = new BoundedPreview<object>
                {
                    TotalRecords = await _Services.Database.FeedPosts.CountModerationActionsAsync(TargetTypeEnum.FeedPost, post.Id, token).ConfigureAwait(false),
                    Enumerate = Constants.ApiPrefix + "/admin/moderation/actions/enumerate"
                };
            }
            detail.ReportSummary = await _Services.Database.FeedPosts.ReadReportSummaryAsync(TargetTypeEnum.FeedPost, post.Id, token).ConfigureAwait(false);
            return detail;
        }

        /// <summary>
        /// POST /api/v1/admin/feed: create a post as the caller or as authorUserId (who must have canPostToFeed or be a
        /// SystemAdmin). Needs a member session: the break-glass token cannot author (403).
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The post.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed (fieldErrors.authorUserId), MediaLimitExceeded, or ContentFiltered;
        /// 403 Forbidden; 409 MediaNotAttachable or PinLimitReached; 429 RateLimited.</exception>
        public async Task<FeedPostView> AdminCreateAsync(ContentActor actor, RequestInfo info, FeedPostRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            string callerId = actor.RequireUserId();
            if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Only a SystemAdmin can use the admin feed.");
            ContentActor author = actor;
            if (!String.IsNullOrWhiteSpace(request.AuthorUserId) && !String.Equals(request.AuthorUserId, callerId, StringComparison.Ordinal))
            {
                try
                {
                    author = await _Services.Actors.ForUserAsync(request.AuthorUserId.Trim(), token).ConfigureAwait(false);
                }
                catch (ApiErrorException)
                {
                    throw ApiErrorException.Field("authorUserId", "Unknown member.");
                }
                if (!author.IsSystemAdmin && !author.CanPostToFeed) throw ApiErrorException.Field("authorUserId", "This member cannot post to the feed (canPostToFeed is off).");
            }
            return await CreateCoreAsync(actor, author, info, request, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/feed/{id}/publish-now: publish a Scheduled post immediately (it still waits for its media).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="postId">Post identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The post (Published, or Processing while media is not Ready).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">404 NotFound, 409 NotScheduled.</exception>
        public async Task<FeedPostView> PublishNowAsync(ContentActor actor, RequestInfo info, string postId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            FeedPost post = await RequirePostAsync(postId, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            if (post.StateAt(now) == FeedPostStateEnum.Deleted) throw ApiErrorException.NotFound("Feed post");
            if (post.StateAt(now) != FeedPostStateEnum.Scheduled) throw new ApiErrorException(409, "NotScheduled", "Only scheduled posts can be published now.");
            post.PublishAtUtc = now;
            post.UpdatedUtc = now;
            await _Services.Database.FeedPosts.UpdateAsync(post, null, token).ConfigureAwait(false);
            await TryPublishAsync(post.Id, token).ConfigureAwait(false);
            return await ViewAsync(await RequirePostAsync(post.Id, token).ConfigureAwait(false), actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// GET /api/v1/admin/metrics/engagement: daily posts, comments, and reactions, active members, open reports, and
        /// scheduled posts (days 7 to 365, default 30).
        /// </summary>
        /// <param name="days">Window in days, or null for 30.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metrics.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed (fieldErrors.days).</exception>
        public async Task<EngagementMetrics> EngagementAsync(int? days, CancellationToken token = default)
        {
            int window = days ?? 30;
            if (window < 7 || window > 365) throw ApiErrorException.Field("days", "Must be 7 to 365.");
            DateTime now = DateTime.UtcNow;
            DateTime from = DateTime.SpecifyKind(now.Date.AddDays(-(window - 1)), DateTimeKind.Utc);
            EngagementMetrics metrics = await _Services.Database.FeedPosts.EngagementAsync(from, window, now, token).ConfigureAwait(false);
            metrics.ThreadsByDay = await _Services.Database.Threads.DailyAsync("threads", from, window, token).ConfigureAwait(false);
            metrics.ThreadCommentsByDay = await _Services.Database.Threads.DailyAsync("comments", from, window, token).ConfigureAwait(false);
            return metrics;
        }

        #endregion

        #region Publishing

        /// <summary>
        /// Publish a post when it is due: not published, not deleted, unmoderated, publishAtUtc null or passed, and every
        /// attachment Ready. publishedUtc is the later of publishAtUtc and the time the last attachment became Ready
        /// (never in the future). Publishing writes the feedPost.publish audit record, starts the NewFeedPost fan-out
        /// (in-app, push, and feed post emails), sends the pending mention notifications, and copies media of Public
        /// posts to the public paths. Safe to call concurrently: only one caller publishes.
        /// </summary>
        /// <param name="postId">Post identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call published the post.</returns>
        public async Task<bool> TryPublishAsync(string postId, CancellationToken token = default)
        {
            FeedPost? post = String.IsNullOrEmpty(postId) ? null : await _Services.Database.FeedPosts.ReadAsync(postId, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            if (post == null || post.PublishedUtc != null || post.DeletedUtc != null || post.ModerationState != ModerationStateEnum.None) return false;
            if (post.PublishAtUtc != null && post.PublishAtUtc.Value > now) return false;
            List<MediaAttachment> media = await _Services.Database.Media.ListByTargetsAsync(TargetTypeEnum.FeedPost, new[] { post.Id }, token).ConfigureAwait(false);
            if (media.Any(m => m.Status != MediaStatusEnum.Ready)) return false;
            using (Activity? activity = TrellisKitTelemetry.StartSpan("feed.publish"))
            {
                activity?.SetTag("feed.post_id", post.Id);
                DateTime published = post.PublishAtUtc ?? DateTime.MinValue;
                foreach (MediaAttachment m in media) if (m.ReadyUtc != null && m.ReadyUtc.Value > published) published = m.ReadyUtc.Value;
                if (published == DateTime.MinValue || published > now) published = now;
                if (!await _Services.Database.FeedPosts.TryPublishAsync(post.Id, published, now, token).ConfigureAwait(false)) return false;
                post.PublishedUtc = published;
                await _Services.Audit.WriteAsync(null, null, "feedPost.publish", TargetTypeEnum.FeedPost.ToString(), post.Id, Summary(post), token).ConfigureAwait(false);
                await _Services.Notifications.StartFanoutAsync(NotificationTypeEnum.NewFeedPost, post.Id, token).ConfigureAwait(false);
                List<NotificationDraft> drafts = await _Services.Mentions.ClaimDraftsAsync(TargetTypeEnum.FeedPost, post.Id, post.AuthorUserId, post.Id, post.Excerpt, token).ConfigureAwait(false);
                if (drafts.Count > 0) await _Services.Notifications.CreateForSourceAsync(drafts, token).ConfigureAwait(false);
                await SyncPublicMediaAsync(post, token).ConfigureAwait(false);
                Count("publish", "success");
                activity?.SetStatus(ActivityStatusCode.Ok);
                return true;
            }
        }

        /// <summary>
        /// Publish every due post (the publish worker's pass).
        /// </summary>
        /// <param name="limit">Most posts per pass.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Posts published.</returns>
        public async Task<int> PublishDueAsync(int limit, CancellationToken token = default)
        {
            int published = 0;
            foreach (FeedPost post in await _Services.Database.FeedPosts.ListPublishCandidatesAsync(DateTime.UtcNow, Math.Max(1, limit), token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                if (await TryPublishAsync(post.Id, token).ConfigureAwait(false)) published++;
            }
            return published;
        }

        #endregion

        #region Moderation-Hooks

        /// <summary>
        /// Moderation hook: set a post's moderation state (None, Hidden, Removed) with moderatedUtc, moderatedByUserId,
        /// and moderationReason. Hidden and Removed posts leave the feed, lose their pin, and have their public media
        /// copies deleted; None restores them (public copies are recreated for Public posts). The caller (moderation
        /// feature) checks scope, writes the moderation action, retains or restores media, and notifies.
        /// </summary>
        /// <param name="postId">Post identifier.</param>
        /// <param name="state">New state.</param>
        /// <param name="moderatorUserId">Moderator, or null for the system (auto-hide).</param>
        /// <param name="reason">Reason, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored post.</returns>
        /// <exception cref="ApiErrorException">404 NotFound (unknown or deleted).</exception>
        public async Task<FeedPost> SetModerationStateAsync(string postId, ModerationStateEnum state, string? moderatorUserId, string? reason, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(postId) || !await _Services.Database.FeedPosts.SetModerationStateAsync(postId, state, moderatorUserId, reason, DateTime.UtcNow, token).ConfigureAwait(false))
                throw ApiErrorException.NotFound("Feed post");
            FeedPost post = await RequirePostAsync(postId, token).ConfigureAwait(false);
            await SyncPublicMediaAsync(post, token).ConfigureAwait(false);
            if (state == ModerationStateEnum.None && post.PublishedUtc == null) PublishSignal?.Invoke();
            return post;
        }

        /// <summary>
        /// Moderation hook: the FeedPost payload for a moderator (full content in any state, including deleted, with
        /// moderation: { state, reason, actedUtc } always present).
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="postId">Post identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payload.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<FeedPostView> ModeratorViewAsync(ContentActor actor, string postId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            FeedPost post = await RequirePostAsync(postId, token).ConfigureAwait(false);
            return (await _Services.ContentViews.FeedPostsAsync(new[] { post }, actor, true, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// Moderation hook: recompute feedposts.openreportcount from the Open reports of the post (call after creating
        /// or resolving a report in the same request).
        /// </summary>
        /// <param name="postId">Post identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored count.</returns>
        public Task<int> RecomputeOpenReportCountAsync(string postId, CancellationToken token = default)
        {
            return _Services.Database.FeedPosts.RecomputeOpenReportCountAsync(postId, token);
        }

        /// <summary>
        /// Soft-delete a post without permission checks (account deletion and moderation): see <see cref="DeleteAsync"/>.
        /// </summary>
        /// <param name="post">Post.</param>
        /// <param name="byUserId">Who deleted it, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call deleted the post.</returns>
        /// <exception cref="ArgumentNullException">The post is null.</exception>
        public async Task<bool> DeleteCoreAsync(FeedPost post, string? byUserId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(post);
            using (Activity? activity = TrellisKitTelemetry.StartSpan("feed.delete"))
            {
                bool deleted = await _Services.Database.FeedPosts.SoftDeleteAsync(post.Id, byUserId, DateTime.UtcNow, token).ConfigureAwait(false);
                if (deleted)
                {
                    await _Services.Media.UnpublishTargetAsync(TargetTypeEnum.FeedPost, post.Id, token).ConfigureAwait(false);
                    Count("delete", "success");
                }
                activity?.SetStatus(ActivityStatusCode.Ok);
                return deleted;
            }
        }

        #endregion

        /// <summary>
        /// Whether a post is visible on the public feed: Published, unmoderated, not deleted, visibility and audience
        /// Public.
        /// </summary>
        /// <param name="post">Post.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>True when public.</returns>
        /// <exception cref="ArgumentNullException">The post is null.</exception>
        public static bool IsPublic(FeedPost post, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(post);
            return post.StateAt(nowUtc) == FeedPostStateEnum.Published && post.Visibility == FeedVisibilityEnum.Public && post.Audience == ContentAudienceEnum.Public;
        }

        #endregion

        #region Private-Methods

        private async Task<FeedPostView> CreateCoreAsync(ContentActor caller, ContentActor author, RequestInfo info, FeedPostRequest request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            using (Activity? activity = TrellisKitTelemetry.StartSpan("feed.create"))
            {
                try
                {
                    _Services.Limits.EnforceMember(caller, PostsBucket, _Services.Settings.Feed.PostsPerHour, RateLimitService.Hour);
                    DateTime now = DateTime.UtcNow;
                    Dictionary<string, string> errors = new Dictionary<string, string>();
                    string? title = NormalizeTitle(request.Title, errors);
                    string body = NormalizeBody(request.Body, _Services.Settings.Feed.MaxBodyLength, errors);
                    ContentAudienceEnum audience = DefaultAudience(caller);
                    FeedVisibilityEnum visibility = audience == ContentAudienceEnum.Public ? FeedVisibilityEnum.Public : FeedVisibilityEnum.Members;
                    ResolveAudience(caller, request.Visibility, request.Audience, ref visibility, ref audience, errors);
                    DateTime? publishAt = ValidatePublishAt(request.PublishAtUtc, now, errors);
                    List<string> mediaIds = (request.MediaIds ?? new List<string>()).ToList();
                    if (body.Length == 0 && mediaIds.Count == 0) errors.TryAdd("body", "Write something or attach media.");
                    if (errors.Count > 0) throw ApiErrorException.Validation(errors);
                    List<MediaAttachment> media = await _Services.Media.ValidateAttachableAsync(caller, mediaIds, _Services.Settings.Feed.MaxAttachments, _Services.Settings.Feed.MaxVideosPerPost, null, null, token).ConfigureAwait(false);
                    // Only Published posts can be pinned (contract 5.8): a scheduled post is pinned with the pin route after it publishes.
                    bool pin = request.Pin == true && caller.IsSystemAdmin && publishAt == null;
                    if (pin && await _Services.Database.FeedPosts.CountPinnedAsync(token).ConfigureAwait(false) >= _Services.Settings.Feed.MaxPinned)
                        throw new ApiErrorException(409, "PinLimitReached", "At most " + _Services.Settings.Feed.MaxPinned + " posts can be pinned. Unpin one first.");
                    WordFilterResult filter = await _Services.WordFilter.CheckAsync(author, TargetTypeEnum.FeedPost, FilterFields(title, body, media), token).ConfigureAwait(false);
                    await _Services.Metadata.ValidateAsync(author, request.Labels, request.Tags, author.IsStaffFor(TargetTypeEnum.FeedPost), token, TargetTypeEnum.FeedPost).ConfigureAwait(false);

                    string id = IdGenerator.Generate(Constants.FeedPostIdPrefix);
                    if (media.Count > 0) await _Services.Media.AttachAsync(TargetTypeEnum.FeedPost, id, media.Select(m => m.Id).ToList(), token).ConfigureAwait(false);
                    bool showPreview = request.ShowLinkPreview ?? true;
                    FeedPost post = new FeedPost
                    {
                        Id = id,
                        AuthorUserId = author.UserId!,
                        Title = title,
                        Body = body,
                        Excerpt = MarkdownNormalizer.Excerpt(body),
                        Visibility = visibility,
                        Audience = audience,
                        LinkPreviewId = await _Services.LinkPreviews.ResolveForBodyAsync(body, showPreview, token).ConfigureAwait(false),
                        ShowLinkPreview = showPreview,
                        CommentsEnabled = request.CommentsEnabled ?? true,
                        PublishAtUtc = publishAt,
                        IsPinned = pin,
                        PinnedUtc = pin ? now : null,
                        PinnedByUserId = pin ? caller.UserId : null,
                        AttachmentCount = media.Count,
                        HasVideo = media.Any(m => m.Kind == MediaKindEnum.Video),
                        CreatedUtc = now,
                        UpdatedUtc = now
                    };
                    post = await _Services.Database.FeedPosts.CreateAsync(post, token).ConfigureAwait(false);
                    await _Services.Metadata.ApplyAsync(author, TargetTypeEnum.FeedPost, id, request.Labels, request.Tags, token).ConfigureAwait(false);
                    await _Services.WordFilter.RecordFlagsAsync(filter, TargetTypeEnum.FeedPost, id, post.AuthorUserId, token).ConfigureAwait(false);
                    await _Services.Mentions.StoreAsync(body, TargetTypeEnum.FeedPost, id, post.AuthorUserId, token).ConfigureAwait(false);
                    await _Services.Audit.WriteAsync(info, "feedPost.create", TargetTypeEnum.FeedPost.ToString(), id, Summary(post), token).ConfigureAwait(false);
                    Count("create", "success");
                    if (publishAt == null) await TryPublishAsync(id, token).ConfigureAwait(false);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return await ViewAsync(await RequirePostAsync(id, token).ConfigureAwait(false), caller, token).ConfigureAwait(false);
                }
                catch (ApiErrorException)
                {
                    Count("create", "rejected");
                    throw;
                }
            }
        }

        private async Task<FeedPostView> UpdateCoreAsync(ContentActor actor, RequestInfo info, string postId, FeedPostRequest request, HashSet<string> present, CancellationToken token)
        {
            FeedPost post = await RequireOwnedAsync(actor, postId, token).ConfigureAwait(false);
            actor.RequireCanPost();
            _Services.Limits.EnforceMember(actor, EditsBucket, _Services.Settings.RateLimits.EditsPerHour, RateLimitService.Hour);
            DateTime now = DateTime.UtcNow;
            FeedPostStateEnum state = post.StateAt(now);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string? title = present.Contains("title") ? NormalizeTitle(request.Title, errors) : post.Title;
            string body = present.Contains("body") ? NormalizeBody(request.Body, _Services.Settings.Feed.MaxBodyLength, errors) : post.Body;
            FeedVisibilityEnum visibility = post.Visibility;
            ContentAudienceEnum audience = post.Audience;
            if (present.Contains("visibility") || present.Contains("audience")) ResolveAudience(actor, request.Visibility, request.Audience, ref visibility, ref audience, errors);
            DateTime? publishAt = post.PublishAtUtc;
            bool publishNow = false;
            if (present.Contains("publishAtUtc"))
            {
                if (state != FeedPostStateEnum.Scheduled) errors["publishAtUtc"] = "Only scheduled posts can be rescheduled.";
                else if (request.PublishAtUtc == null)
                {
                    publishAt = null;
                    publishNow = true;
                }
                else publishAt = ValidatePublishAt(request.PublishAtUtc, now, errors);
            }
            List<MediaAttachment> before = await _Services.Database.Media.ListByTargetsAsync(TargetTypeEnum.FeedPost, new[] { post.Id }, token).ConfigureAwait(false);
            List<MediaAttachment> after = before;
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (present.Contains("mediaIds"))
            {
                after = await _Services.Media.ValidateAttachableAsync(actor, (request.MediaIds ?? new List<string>()).ToList(), _Services.Settings.Feed.MaxAttachments,
                    _Services.Settings.Feed.MaxVideosPerPost, TargetTypeEnum.FeedPost, post.Id, token).ConfigureAwait(false);
            }
            if (body.Length == 0 && after.Count == 0) throw ApiErrorException.Field("body", "Write something or attach media.");
            ContentActor author = String.Equals(actor.UserId, post.AuthorUserId, StringComparison.Ordinal) ? actor : await AuthorActorAsync(post.AuthorUserId, actor, token).ConfigureAwait(false);
            // The filter judges the author's words, so a SystemAdmin editing a member's post is still filtered as the member.
            WordFilterResult filter = await _Services.WordFilter.CheckAsync(author, TargetTypeEnum.FeedPost, FilterFields(title, body, after), token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.FeedPost), token, TargetTypeEnum.FeedPost).ConfigureAwait(false);

            bool mediaChanged = AttachmentSummaryText.Differs(before, after);
            bool bodyChanged = !String.Equals(body, post.Body, StringComparison.Ordinal);
            bool contentChanged = bodyChanged || !String.Equals(title, post.Title, StringComparison.Ordinal) || mediaChanged;
            bool silent = post.PublishedUtc == null || (now - post.PublishedUtc.Value).TotalSeconds <= _Services.Settings.Content.EditGraceSeconds;
            FeedPostRevision? revision = contentChanged && !silent ? new FeedPostRevision
            {
                Id = IdGenerator.Generate(Constants.FeedPostRevisionIdPrefix),
                FeedPostId = post.Id,
                Title = post.Title,
                Body = post.Body,
                AttachmentSummary = AttachmentSummaryText.Describe(before, after),
                EditedByUserId = actor.UserId,
                CreatedUtc = now
            } : null;
            if (present.Contains("mediaIds") && mediaChanged) await _Services.Media.AttachAsync(TargetTypeEnum.FeedPost, post.Id, after.Select(m => m.Id).ToList(), token).ConfigureAwait(false);
            bool showPreview = present.Contains("showLinkPreview") ? (request.ShowLinkPreview ?? true) : post.ShowLinkPreview;
            if (bodyChanged || showPreview != post.ShowLinkPreview) post.LinkPreviewId = await _Services.LinkPreviews.ResolveForBodyAsync(body, showPreview, token).ConfigureAwait(false);
            bool wasPublic = IsPublic(post, now);
            post.Title = title;
            post.Body = body;
            post.Excerpt = MarkdownNormalizer.Excerpt(body);
            post.Visibility = visibility;
            post.Audience = audience;
            post.ShowLinkPreview = showPreview;
            if (present.Contains("commentsEnabled")) post.CommentsEnabled = request.CommentsEnabled ?? true;
            post.PublishAtUtc = publishAt;
            if (revision != null) post.EditedUtc = now;
            post.UpdatedUtc = now;
            post = await _Services.Database.FeedPosts.UpdateAsync(post, revision, token).ConfigureAwait(false);
            if (revision != null) await _Services.Metadata.SnapshotAsync(TargetTypeEnum.FeedPost, post.Id, TargetTypeEnum.FeedPostRevision, revision.Id, token).ConfigureAwait(false);
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.FeedPost, post.Id, present.Contains("labels") ? request.Labels : null, present.Contains("tags") ? request.Tags : null, token).ConfigureAwait(false);
            await _Services.WordFilter.RecordFlagsAsync(filter, TargetTypeEnum.FeedPost, post.Id, post.AuthorUserId, token).ConfigureAwait(false);
            if (bodyChanged)
            {
                await _Services.Mentions.StoreAsync(body, TargetTypeEnum.FeedPost, post.Id, author.UserId ?? post.AuthorUserId, token).ConfigureAwait(false);
                if (post.PublishedUtc != null)
                {
                    List<NotificationDraft> drafts = await _Services.Mentions.ClaimDraftsAsync(TargetTypeEnum.FeedPost, post.Id, post.AuthorUserId, post.Id, post.Excerpt, token).ConfigureAwait(false);
                    if (drafts.Count > 0) await _Services.Notifications.CreateForSourceAsync(drafts, token).ConfigureAwait(false);
                }
            }
            if (wasPublic || IsPublic(post, now)) await SyncPublicMediaAsync(post, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "feedPost.update", TargetTypeEnum.FeedPost.ToString(), post.Id, Summary(post) + (revision != null ? " (revision)" : String.Empty), token).ConfigureAwait(false);
            if (publishNow || (post.PublishedUtc == null && mediaChanged)) await TryPublishAsync(post.Id, token).ConfigureAwait(false);
            return await ViewAsync(await RequirePostAsync(post.Id, token).ConfigureAwait(false), actor, token).ConfigureAwait(false);
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

        private async Task<FeedPost> RequireOwnedAsync(ContentActor actor, string postId, CancellationToken token)
        {
            FeedPost post = await RequirePostAsync(postId, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            if (post.DeletedUtc != null) throw ApiErrorException.NotFound("Feed post");
            bool isAuthor = actor.UserId != null && String.Equals(post.AuthorUserId, actor.UserId, StringComparison.Ordinal);
            if (actor.IsSystemAdmin || isAuthor) return post;
            if (ContentAccessService.CanSeePost(post, actor, now)) throw ApiErrorException.Forbidden("Only the author or a SystemAdmin can change this post.");
            throw ApiErrorException.NotFound("Feed post");
        }

        private async Task<FeedPost> RequirePostAsync(string postId, CancellationToken token)
        {
            FeedPost? post = String.IsNullOrWhiteSpace(postId) ? null : await _Services.Database.FeedPosts.ReadAsync(postId, token).ConfigureAwait(false);
            if (post == null) throw ApiErrorException.NotFound("Feed post");
            return post;
        }

        private async Task<FeedPostView> ViewAsync(FeedPost post, ContentActor actor, CancellationToken token)
        {
            return (await _Services.ContentViews.FeedPostsAsync(new[] { post }, actor, false, token).ConfigureAwait(false))[0];
        }

        private static List<ContentAudienceEnum> ListedAudiences(ContentActor actor, ContentAudienceEnum? requested)
        {
            List<ContentAudienceEnum> visible = ContentAudienceRule.ListedAudiences(actor);
            if (requested == null) return visible;
            return visible.Contains(requested.Value) ? new List<ContentAudienceEnum> { requested.Value } : new List<ContentAudienceEnum>();
        }

        private static void CheckWindow(DateTime? afterUtc, DateTime? beforeUtc, string afterName, string beforeName, Dictionary<string, string> errors)
        {
            if (afterUtc != null && beforeUtc != null && afterUtc.Value >= beforeUtc.Value)
                errors.TryAdd("filters." + beforeName, "Must be after " + afterName + ".");
        }

        private static DateTime? AliasUtc(EnumerationQuery q, string name, string alias, Dictionary<string, string> errors)
        {
            DateTime? primary = q.FilterUtc(name, errors);
            DateTime? secondary = q.FilterUtc(alias, errors);
            if (primary != null && secondary != null && primary.Value != secondary.Value) errors["filters." + alias] = "Send " + name + " or its alias " + alias + ", not both with different values.";
            return primary ?? secondary;
        }

        private async Task<List<FeedPostRevisionView>> RevisionViewsAsync(List<FeedPostRevision> revisions, bool includeStaff, CancellationToken token)
        {
            Dictionary<string, UserSummary> editors = await _Services.Summaries.BuildAsync(revisions.Select(r => r.EditedByUserId), token).ConfigureAwait(false);
            List<FeedPostRevisionView> views = revisions.Select(r =>
            {
                UserSummary editor = r.EditedByUserId != null && editors.TryGetValue(r.EditedByUserId, out UserSummary? u) ? u : UserSummary.Deleted();
                return new FeedPostRevisionView
                {
                    Id = r.Id,
                    Revision = r.Revision,
                    Title = r.Title,
                    Body = r.Body,
                    AttachmentSummary = r.AttachmentSummary,
                    EditedBy = editor,
                    EditedByUserId = r.EditedByUserId,
                    EditedByName = r.EditedByUserId == null ? "admin token" : editor.DisplayName,
                    CreatedUtc = r.CreatedUtc
                };
            }).ToList();
            return await _Services.Metadata.AttachAsync(TargetTypeEnum.FeedPostRevision, views, v => v.Id, includeStaff, token).ConfigureAwait(false);
        }

        private async Task SyncPublicMediaAsync(FeedPost post, CancellationToken token)
        {
            bool shouldBePublic = _Services.Property.Property.Features.PublicSamples && IsPublic(post, DateTime.UtcNow);
            if (shouldBePublic) await _Services.Media.PublishTargetAsync(TargetTypeEnum.FeedPost, post.Id, token).ConfigureAwait(false);
            else await _Services.Media.UnpublishTargetAsync(TargetTypeEnum.FeedPost, post.Id, token).ConfigureAwait(false);
        }

        private void RequirePublicSamples(string? sourceIp)
        {
            if (!_Services.Property.Property.Features.PublicSamples) throw ApiErrorException.NotFound("Public feed");
            _Services.Limits.EnforceIp(sourceIp, PublicBucket, _Services.Settings.RateLimits.PublicFeedPerMinute, RateLimitService.Minute);
        }

        private async Task OnMediaDetachedAsync(TargetTypeEnum targetType, string targetId, CancellationToken token)
        {
            if (targetType == TargetTypeEnum.FeedPost) await _Services.Database.FeedPosts.RecomputeAttachmentsAsync(targetId, token).ConfigureAwait(false);
            else if (targetType == TargetTypeEnum.Comment) await _Services.Database.Comments.RecomputeAttachmentsAsync(targetId, token).ConfigureAwait(false);
        }

        private async Task OnMediaSettledAsync(MediaAttachment media, CancellationToken token)
        {
            if (media.AttachedTargetType != TargetTypeEnum.FeedPost || String.IsNullOrEmpty(media.AttachedTargetId)) return;
            FeedPost? post = await _Services.Database.FeedPosts.ReadAsync(media.AttachedTargetId, token).ConfigureAwait(false);
            if (post == null) return;
            if (post.PublishedUtc != null)
            {
                if (_Services.Property.Property.Features.PublicSamples && IsPublic(post, DateTime.UtcNow)) await _Services.Media.PublishTargetAsync(TargetTypeEnum.FeedPost, post.Id, token).ConfigureAwait(false);
                return;
            }
            PublishSignal?.Invoke();
        }

        private ContentAudienceEnum DefaultAudience(ContentActor caller)
        {
            // docs/ACCESS_MODELS_CONTRACT.md section 3.3: Members in a gated feature, else the property default; a Public
            // default applies only to SystemAdmin authors (others get the signed-in default).
            AccessPolicy policy = _Services.Access.Current;
            ContentAudienceEnum audience = policy.DefaultAudienceFor(FeatureKeyEnum.Feed);
            if (audience == ContentAudienceEnum.Public && !caller.IsSystemAdmin) audience = policy.Model == AccessModelEnum.FullyGated ? ContentAudienceEnum.Members : ContentAudienceEnum.SignedIn;
            return audience;
        }

        private ContentAudienceEnum NonPublicDefault(ContentActor caller, ContentAudienceEnum current)
        {
            if (current != ContentAudienceEnum.Public) return current;
            ContentAudienceEnum audience = DefaultAudience(caller);
            return audience == ContentAudienceEnum.Public ? (_Services.Access.Current.Model == AccessModelEnum.Open ? ContentAudienceEnum.SignedIn : ContentAudienceEnum.Members) : audience;
        }

        private void ResolveAudience(ContentActor caller, FeedVisibilityEnum? requestedVisibility, ContentAudienceEnum? requestedAudience, ref FeedVisibilityEnum visibility, ref ContentAudienceEnum audience, Dictionary<string, string> errors)
        {
            if (requestedAudience == ContentAudienceEnum.Members && _Services.Access.Current.Model == AccessModelEnum.Open)
            {
                errors["audience"] = "Members is not available: this property has no memberships. Use SignedIn.";
                return;
            }
            if (requestedVisibility != null && requestedAudience != null)
            {
                if ((requestedVisibility == FeedVisibilityEnum.Public) != (requestedAudience == ContentAudienceEnum.Public))
                {
                    errors["audience"] = "Public visibility and the Public audience go together.";
                    return;
                }
                visibility = requestedVisibility.Value;
                audience = requestedAudience.Value;
            }
            else if (requestedVisibility != null)
            {
                // Phase 2 visibility maps onto audience: Public is Public; Members keeps a non-public audience or takes
                // the property's non-public default.
                visibility = requestedVisibility.Value;
                audience = visibility == FeedVisibilityEnum.Public ? ContentAudienceEnum.Public : NonPublicDefault(caller, audience);
            }
            else if (requestedAudience != null)
            {
                audience = requestedAudience.Value;
                visibility = audience == ContentAudienceEnum.Public ? FeedVisibilityEnum.Public : FeedVisibilityEnum.Members;
            }
            if (visibility == FeedVisibilityEnum.Public && !caller.IsSystemAdmin) throw ApiErrorException.Forbidden("Only a SystemAdmin can make a post Public.");
        }

        private static DateTime? ValidatePublishAt(DateTime? value, DateTime now, Dictionary<string, string> errors)
        {
            if (value == null) return null;
            DateTime utc = value.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value.Value.ToUniversalTime();
            if (utc < now.AddMinutes(1) || utc > now.AddDays(365)) errors["publishAtUtc"] = "Must be at least 1 minute and at most 365 days in the future.";
            return utc;
        }

        private static string? NormalizeTitle(string? title, Dictionary<string, string> errors)
        {
            if (title == null) return null;
            if (!MarkdownNormalizer.IsWellFormed(title))
            {
                errors["title"] = "Must be valid UTF-8 text.";
                return null;
            }
            string normalized = MarkdownNormalizer.NormalizeSingleLine(title);
            if (MarkdownNormalizer.Length(normalized) > MaxTitleLength) errors["title"] = "At most " + MaxTitleLength + " characters.";
            return normalized.Length == 0 ? null : normalized;
        }

        /// <summary>
        /// Normalize a body (contract section 3.2) and check its maximum length.
        /// </summary>
        /// <param name="body">Raw body.</param>
        /// <param name="maxLength">Maximum length in Unicode scalar values.</param>
        /// <param name="errors">Field errors (fieldErrors.body).</param>
        /// <returns>Normalized body (empty when absent).</returns>
        internal static string NormalizeBody(string? body, int maxLength, Dictionary<string, string> errors)
        {
            if (body == null) return String.Empty;
            if (!MarkdownNormalizer.IsWellFormed(body))
            {
                errors["body"] = "Must be valid UTF-8 text.";
                return String.Empty;
            }
            string normalized = MarkdownNormalizer.Normalize(body);
            if (MarkdownNormalizer.Length(normalized) > maxLength) errors["body"] = "At most " + maxLength + " characters.";
            return normalized;
        }

        /// <summary>
        /// Word filter fields of a write: title, body, and the alt text of each attachment.
        /// </summary>
        /// <param name="title">Title, or null.</param>
        /// <param name="body">Body.</param>
        /// <param name="media">Attachments in order.</param>
        /// <returns>Fields.</returns>
        internal static Dictionary<string, string?> FilterFields(string? title, string? body, IReadOnlyList<MediaAttachment> media)
        {
            Dictionary<string, string?> fields = new Dictionary<string, string?>();
            if (title != null) fields["title"] = title;
            fields["body"] = body;
            for (int i = 0; i < media.Count; i++) if (!String.IsNullOrEmpty(media[i].AltText)) fields["mediaIds[" + i + "].altText"] = media[i].AltText;
            return fields;
        }

        private static string Summary(FeedPost post)
        {
            string text = post.Title ?? post.Excerpt;
            return text.Length > 120 ? text.Substring(0, 120) : text;
        }

        private static void Count(string action, string outcome)
        {
            TrellisKitTelemetry.Add(TrellisKitTelemetry.FeedPosts, 1, "action", action, "outcome", outcome);
        }

        #endregion
    }
}
