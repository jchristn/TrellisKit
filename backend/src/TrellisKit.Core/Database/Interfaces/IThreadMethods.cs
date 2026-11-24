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
    /// Forum thread, revision, follow, read state, and trending methods (docs/PHASE3_CONTRACT.md sections 4, 5, 9, and 17).
    /// </summary>
    public interface IThreadMethods
    {
        /// <summary>
        /// Create a thread with its optional poll and the author's follow, and recompute the forum counters in the same batch.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="poll">Poll.</param>
        /// <param name="authorFollow">Author follow.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<ForumThread> CreateAsync(ForumThread thread, Poll? poll, ThreadFollow? authorFollow, CancellationToken token = default);

        /// <summary>
        /// Read a thread in any state, or null.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<ForumThread?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read several threads in any state.
        /// </summary>
        /// <param name="ids">Ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<ForumThread>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Read the undeleted discussion thread linked to a source (for example a feed post), or null.
        /// </summary>
        /// <param name="sourceType">Source type.</param>
        /// <param name="sourceId">Source id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<ForumThread?> ReadBySourceAsync(string sourceType, string sourceId, CancellationToken token = default);

        /// <summary>
        /// Store edited thread fields and an optional revision (the revision count is recomputed from the rows).
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="revision">Revision.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<ForumThread> UpdateAsync(ForumThread thread, ThreadRevision? revision, CancellationToken token = default);

        /// <summary>
        /// Recompute the attachment count and video flag from the attached media.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> RecomputeAttachmentsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Publish a Processing thread (conditional), set its initial trending score, and recompute the forum counters.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="publishedUtc">Published utc.</param>
        /// <param name="initialScore">Initial score.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> TryPublishAsync(string id, DateTime publishedUtc, double initialScore, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Unpublished, unmoderated, undeleted threads whose media are all Ready.
        /// </summary>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<ForumThread>> ListPublishCandidatesAsync(int limit, CancellationToken token = default);

        /// <summary>
        /// Soft-delete a thread, clear its pin and score, delete its follows and read state, and recompute the forum counters.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="byUserId">By user id.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> SoftDeleteAsync(string id, string? byUserId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Set the moderation state (Hidden or Removed also clear the pin and score) and recompute the forum counters.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="state">State.</param>
        /// <param name="moderatorUserId">Moderator user id.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> SetModerationStateAsync(string id, ModerationStateEnum state, string? moderatorUserId, string? reason, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Pin a Published thread unless the forum already has the maximum pinned threads (serialized per forum).
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="byUserId">By user id.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="maxPinned">Max pinned.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> PinAsync(string id, string? byUserId, DateTime nowUtc, int maxPinned, CancellationToken token = default);

        /// <summary>
        /// Unpin a thread.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> UnpinAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Lock or unlock a thread.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="locked">Locked.</param>
        /// <param name="byUserId">By user id.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> SetLockedAsync(string id, bool locked, string? byUserId, string? reason, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Move a thread to another forum with its (narrowed) audience, unpin it, move open Forum-scope reports, and recompute both forums.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="toForumId">To forum id.</param>
        /// <param name="audience">Audience.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> MoveAsync(string id, string toForumId, ContentAudienceEnum audience, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Accept a visible comment of the thread as the answer, or clear it (null); serialized per thread.
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <param name="commentId">Comment id.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> SetAcceptedAnswerAsync(string threadId, string? commentId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Set a Feedback thread's status, reviewer, and optional review comment; serialized per thread.
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <param name="status">Status.</param>
        /// <param name="reviewerUserId">Reviewer user id.</param>
        /// <param name="reviewCommentId">Review comment id.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> SetFeedbackAsync(string threadId, FeedbackStatusEnum status, string? reviewerUserId, string? reviewCommentId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Open or InReview Feedback threads by a member, optionally in one forum.
        /// </summary>
        /// <param name="authorUserId">Author user id.</param>
        /// <param name="forumId">Forum id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<long> CountOpenFeedbackAsync(string authorUserId, string? forumId, CancellationToken token = default);

        /// <summary>
        /// Record an active feed card (conditional on none) or clear it (null).
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <param name="feedPostId">Feed post id.</param>
        /// <param name="byUserId">By user id.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> SetPromotionAsync(string threadId, string? feedPostId, string? byUserId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate threads with the filter; Hot and ScoreDescending page by score, last activity, and id; Relevance by the candidate order.
        /// </summary>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<ForumThread>> EnumerateAsync(EnumerationRequest request, ThreadFilter filter, CancellationToken token = default);

        /// <summary>
        /// Pinned Published threads matching the filter, newest pin first.
        /// </summary>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="max">Max.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<ForumThread>> ListPinnedAsync(EnumerationRequest request, ThreadFilter filter, int max, CancellationToken token = default);

        /// <summary>
        /// Pinned Published threads of a forum.
        /// </summary>
        /// <param name="forumId">Forum id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<long> CountPinnedAsync(string forumId, CancellationToken token = default);

        /// <summary>
        /// Enumerate a thread's revisions.
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<ThreadRevision>> EnumerateRevisionsAsync(string threadId, EnumerationRequest request, CancellationToken token = default);

        /// <summary>
        /// Every revision of several threads (export).
        /// </summary>
        /// <param name="threadIds">Thread ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<ThreadRevision>> ListRevisionsAsync(IEnumerable<string> threadIds, CancellationToken token = default);

        /// <summary>
        /// Follow a thread (an explicit follow clears an opt-out; automatic follows respect it), keeping at most maxFollows followed threads.
        /// </summary>
        /// <param name="follow">Follow.</param>
        /// <param name="maxFollows">Max follows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> FollowAsync(ThreadFollow follow, int maxFollows, CancellationToken token = default);

        /// <summary>
        /// Unfollow a thread; the opt-out sticks so automatic follows do not return.
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <param name="userId">User id.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> UnfollowAsync(string threadId, string userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Read a member's follow row of a thread, or null.
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <param name="userId">User id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<ThreadFollow?> ReadFollowAsync(string threadId, string userId, CancellationToken token = default);

        /// <summary>
        /// Threads among the identifiers that the member follows.
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="threadIds">Thread ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<HashSet<string>> FollowedAmongAsync(string userId, IEnumerable<string> threadIds, CancellationToken token = default);

        /// <summary>
        /// Followers of a thread in user id order after a cursor.
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <param name="afterUserId">After user id.</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<string>> FollowerIdsAsync(string threadId, string? afterUserId, int limit, CancellationToken token = default);

        /// <summary>
        /// Followers of a thread.
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<long> CountFollowersAsync(string threadId, CancellationToken token = default);

        /// <summary>
        /// Enumerate a member's forum and thread follows (labels and tags of ForumFollow and ThreadFollow).
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="targetType">Target type.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<FollowRow>> EnumerateFollowsAsync(string userId, EnumerationRequest request, TargetTypeEnum? targetType, CancellationToken token = default);

        /// <summary>
        /// Record that a member opened a thread.
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="threadId">Thread id.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        Task MarkReadAsync(string userId, string threadId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// A member's last read times of several threads.
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="threadIds">Thread ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<Dictionary<string, DateTime>> ReadTimesAsync(string userId, IEnumerable<string> threadIds, CancellationToken token = default);

        /// <summary>
        /// Delete thread read markers (threadreads) and forum read markers (forumreads) older than a cutoff, in one
        /// statement (docs/PHASE3_CONTRACT.md section 9.4).
        /// </summary>
        /// <param name="beforeUtc">Rows last read before this time are deleted.</param>
        /// <param name="limit">Maximum rows per table per call. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted across both tables.</returns>
        Task<int> PruneReadsAsync(DateTime beforeUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Trending inputs of live threads active since the window start, in id order after a cursor.
        /// </summary>
        /// <param name="windowStartUtc">Window start utc.</param>
        /// <param name="afterId">After id.</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<TrendingInput>> TrendingBatchAsync(DateTime windowStartUtc, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Store trending scores.
        /// </summary>
        /// <param name="scores">Scores.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<int> SetScoresAsync(Dictionary<string, double> scores, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Reset the scores of threads outside the window or no longer live.
        /// </summary>
        /// <param name="windowStartUtc">Window start utc.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<int> ZeroOutsideWindowAsync(DateTime windowStartUtc, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Threads with a non-zero score.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<long> CountScoredAsync(CancellationToken token = default);

        /// <summary>
        /// Recompute a thread's open report count.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<int> RecomputeOpenReportCountAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Recompute a thread's comment counters and its forum's counters.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        Task RecomputeCommentsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Raise a thread's last activity and recompute its forum's counters.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="activityUtc">Activity utc.</param>
        /// <param name="token">Cancellation token.</param>
        Task TouchActivityAsync(string id, DateTime activityUtc, CancellationToken token = default);

        /// <summary>
        /// Original threads by a member in id order after a cursor (export and erasure).
        /// </summary>
        /// <param name="authorUserId">Author user id.</param>
        /// <param name="afterId">After id.</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="includeDeleted">Include deleted.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<ForumThread>> ListByAuthorAsync(string authorUserId, string? afterId, int limit, bool includeDeleted, CancellationToken token = default);

        /// <summary>
        /// Delete a member's follows, read state, and forum member preferences.
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<int> DeleteFollowsForUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Purge a removed thread's content after the retention period.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> PurgeAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Active threads in the window, open review requests, and unanswered questions.
        /// </summary>
        /// <param name="fromUtc">From utc.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<ThreadActivityCounts> ActivityCountsAsync(DateTime fromUtc, CancellationToken token = default);

        /// <summary>
        /// A daily series (threads, comments, or votes) from a start day.
        /// </summary>
        /// <param name="what">What.</param>
        /// <param name="fromUtc">From utc.</param>
        /// <param name="days">Days.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<DailyCount>> DailyAsync(string what, DateTime fromUtc, int days, CancellationToken token = default);

        /// <summary>
        /// Threads published since a time, per forum.
        /// </summary>
        /// <param name="fromUtc">From utc.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<Dictionary<string, long>> ThreadsInWindowByForumAsync(DateTime fromUtc, CancellationToken token = default);

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
