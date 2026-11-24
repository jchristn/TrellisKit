namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Feed post and feed post revision data access. Implementations are safe to call concurrently: counters are
    /// recomputed from source rows inside the write batch, and state changes are conditional updates.
    /// </summary>
    public interface IFeedPostMethods
    {
        /// <summary>
        /// Insert a post.
        /// </summary>
        /// <param name="post">Post (id, author, body, and excerpt set).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored post.</returns>
        /// <exception cref="ArgumentNullException">The post or a required field is null or empty.</exception>
        Task<FeedPost> CreateAsync(FeedPost post, CancellationToken token = default);

        /// <summary>
        /// Read a post in any state (including deleted).
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Post, or null.</returns>
        Task<FeedPost?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read many posts in any state.
        /// </summary>
        /// <param name="ids">Identifiers (a bounded page).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Posts found.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        Task<List<FeedPost>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Write a post's editable columns (title, body, excerpt, visibility, audience, link preview, flags,
        /// publishAtUtc, editedUtc, updatedUtc) and, when a revision is given, insert it with the next revision number
        /// and recompute revisioncount, in one transaction.
        /// </summary>
        /// <param name="post">Post with the new values.</param>
        /// <param name="revision">The previous version to keep, or null for a silent edit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored post.</returns>
        /// <exception cref="ArgumentNullException">The post is null.</exception>
        Task<FeedPost> UpdateAsync(FeedPost post, FeedPostRevision? revision, CancellationToken token = default);

        /// <summary>
        /// Recompute attachmentcount and hasvideo from the attached, not deleted media rows.
        /// </summary>
        /// <param name="id">Post identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the post exists.</returns>
        Task<bool> RecomputeAttachmentsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Set publishedutc once: only while it is null, the post is not deleted, its moderation state is None, and
        /// publishAtUtc (when set) has passed.
        /// </summary>
        /// <param name="id">Post identifier.</param>
        /// <param name="publishedUtc">Publish time to store.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call published the post.</returns>
        Task<bool> TryPublishAsync(string id, DateTime publishedUtc, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Pin a Published post while fewer than maxPinned other posts are pinned. Idempotent.
        /// </summary>
        /// <param name="id">Post identifier.</param>
        /// <param name="byUserId">Who pinned it, or null.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="maxPinned">feed.maxPinned.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the post is pinned afterwards.</returns>
        Task<bool> PinAsync(string id, string? byUserId, DateTime nowUtc, int maxPinned, CancellationToken token = default);

        /// <summary>
        /// Unpin a post. Idempotent.
        /// </summary>
        /// <param name="id">Post identifier.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the post exists.</returns>
        Task<bool> UnpinAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Soft-delete a post (deletedutc, deletedbyuserid, pin cleared) and delete its labels and tags in the same
        /// transaction.
        /// </summary>
        /// <param name="id">Post identifier.</param>
        /// <param name="byUserId">Who deleted it, or null.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call deleted the post.</returns>
        Task<bool> SoftDeleteAsync(string id, string? byUserId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Set the moderation state (None, Hidden, Removed) with moderatedutc, moderatedbyuserid, and moderationreason.
        /// </summary>
        /// <param name="id">Post identifier.</param>
        /// <param name="state">New state.</param>
        /// <param name="moderatorUserId">Moderator, or null for the system.</param>
        /// <param name="reason">Reason, or null.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the post exists and is not deleted.</returns>
        Task<bool> SetModerationStateAsync(string id, ModerationStateEnum state, string? moderatorUserId, string? reason, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Recompute openreportcount from the Open rows of the reports table.
        /// </summary>
        /// <param name="id">Post identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored count (0 when the post does not exist).</returns>
        Task<int> RecomputeOpenReportCountAsync(string id, CancellationToken token = default);

        /// <summary>
        /// One page of the member or public feed (Published, not deleted, moderation None).
        /// </summary>
        /// <param name="request">Normalized request (ContentCollections.Feed or PublicFeed).</param>
        /// <param name="filter">Typed conditions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<FeedPost>> EnumerateFeedAsync(EnumerationRequest request, FeedPostFilter filter, CancellationToken token = default);

        /// <summary>
        /// Pinned Published posts for the first page of the Feed ordering, pinnedUtc descending, with the request's
        /// search, window, label, and tag conditions.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="filter">Typed conditions.</param>
        /// <param name="max">Most posts (feed.maxPinned).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Pinned posts.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<List<FeedPost>> ListPinnedAsync(EnumerationRequest request, FeedPostFilter filter, int max, CancellationToken token = default);

        /// <summary>
        /// One page of an author's posts in every state except Deleted.
        /// </summary>
        /// <param name="authorUserId">Author.</param>
        /// <param name="request">Normalized request (ContentCollections.MyFeedPosts).</param>
        /// <param name="state">State filter, or null.</param>
        /// <param name="audience">Audience filter, or null.</param>
        /// <param name="nowUtc">Current time (Scheduled versus Processing).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<FeedPost>> EnumerateByAuthorAsync(string authorUserId, EnumerationRequest request, FeedPostStateEnum? state, ContentAudienceEnum? audience, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// One page of the admin feed (every state, including Deleted).
        /// </summary>
        /// <param name="request">Normalized request (ContentCollections.AdminFeed).</param>
        /// <param name="filter">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with exact totals.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<FeedPost>> EnumerateAdminAsync(EnumerationRequest request, AdminFeedFilter filter, CancellationToken token = default);

        /// <summary>
        /// Posts ready to publish, oldest first: unpublished, not deleted, unmoderated, publishAtUtc null or passed, and
        /// every attachment Ready (posts waiting for media or with failed media are not returned).
        /// </summary>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="limit">Most posts.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Candidates.</returns>
        Task<List<FeedPost>> ListPublishCandidatesAsync(DateTime nowUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// One page of a post's revisions.
        /// </summary>
        /// <param name="feedPostId">Post identifier.</param>
        /// <param name="request">Normalized request (ContentCollections.FeedPostRevisions).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<FeedPostRevision>> EnumerateRevisionsAsync(string feedPostId, EnumerationRequest request, CancellationToken token = default);

        /// <summary>
        /// Every revision of a bounded batch of posts, oldest first (account export).
        /// </summary>
        /// <param name="feedPostIds">Post identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Revisions.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        Task<List<FeedPostRevision>> ListRevisionsAsync(IEnumerable<string> feedPostIds, CancellationToken token = default);

        /// <summary>
        /// Open and total report counts of a target (raw counts on the reports table).
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts.</returns>
        Task<ReportSummaryView> ReadReportSummaryAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default);

        /// <summary>
        /// Pinned, not deleted posts (for the feed.maxPinned check).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountPinnedAsync(CancellationToken token = default);

        /// <summary>
        /// Link a discussion thread to a post (conditional on none), or clear the link (null) (Phase 3 section 8.2).
        /// </summary>
        /// <param name="id">Post identifier.</param>
        /// <param name="threadId">Discussion thread, or null to clear.</param>
        /// <param name="byUserId">Who linked it.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the link changed.</returns>
        Task<bool> SetDiscussionAsync(string id, string? threadId, string? byUserId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Mark a promoted card's source as live (false) or not live (true) (docs/PHASE4_CONTRACT.md section 5.8).
        /// </summary>
        /// <param name="id">Card identifier.</param>
        /// <param name="sourceHidden">Whether the source is not live.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the card exists.</returns>
        Task<bool> SetSourceHiddenAsync(string id, bool sourceHidden, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// The live (not deleted) promoted card of a source object, or null.
        /// </summary>
        /// <param name="kind">Card kind (PromotedLesson, PromotedEvent).</param>
        /// <param name="sourceType">Source type name.</param>
        /// <param name="sourceId">Source identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Card or null.</returns>
        Task<FeedPost?> ReadPromotedBySourceAsync(FeedPostKindEnum kind, string sourceType, string sourceId, CancellationToken token = default);

        /// <summary>
        /// Moderation actions recorded on a target (raw count on the moderationactions table).
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountModerationActionsAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default);

        /// <summary>
        /// Engagement metrics for the admin Home charts: published posts, comments, and reactions per UTC day since
        /// fromUtc, members who commented or reacted, open reports, and scheduled posts.
        /// </summary>
        /// <param name="fromUtc">Window start (the first day at 00:00 UTC).</param>
        /// <param name="days">Days in the window.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metrics with one entry per day.</returns>
        Task<EngagementMetrics> EngagementAsync(DateTime fromUtc, int days, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// A keyset batch of an author's posts by identifier (account deletion and export).
        /// </summary>
        /// <param name="authorUserId">Author.</param>
        /// <param name="afterId">Continue after this identifier, or null.</param>
        /// <param name="limit">Most posts.</param>
        /// <param name="includeDeleted">Whether to include soft-deleted posts.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Posts in identifier order.</returns>
        /// <exception cref="ArgumentNullException">The author is null or empty.</exception>
        Task<List<FeedPost>> ListByAuthorAsync(string authorUserId, string? afterId, int limit, bool includeDeleted, CancellationToken token = default);

        /// <summary>
        /// Purge a removed or deleted post (moderation purge worker): title, body, excerpt, and link preview cleared,
        /// revisions, mentions, labels, and tags deleted, revisioncount reset, and purgedutc set, in one transaction.
        /// Idempotent (an already purged post is left alone).
        /// </summary>
        /// <param name="id">Post identifier.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call purged the post.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task<bool> PurgeAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Rows whose stored excerpt still holds Markdown syntax that plain text drops (backticks, pipes, fences, rules,
        /// leading #), in identifier order after <paramref name="afterId"/>, so the excerpt can be recomputed.
        /// </summary>
        /// <param name="afterId">Keyset position (last identifier of the previous batch), or null to start.</param>
        /// <param name="max">Rows per batch. Minimum 1, maximum 1000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Candidates (empty when none are left).</returns>
        Task<List<ExcerptSource>> ExcerptCandidatesAsync(string? afterId, int max, CancellationToken token = default);

        /// <summary>
        /// Replace the stored excerpt of one row.
        /// </summary>
        /// <param name="id">Row identifier.</param>
        /// <param name="excerpt">New excerpt.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpdateExcerptAsync(string id, string excerpt, CancellationToken token = default);
    }
}
