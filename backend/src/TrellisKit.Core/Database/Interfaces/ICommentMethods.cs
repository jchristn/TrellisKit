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
    /// Comment and comment revision data access. Every write that changes visibility (create, delete, erase,
    /// moderation) recomputes childcount, descendantcount, and score of every ancestor in the comment's path and the
    /// target's commentcount from the comment rows in the same transaction, after locking the target row, so concurrent
    /// writes on one post never drift a counter. Implementations are safe to call concurrently.
    /// </summary>
    public interface ICommentMethods
    {
        /// <summary>
        /// Insert a comment (path, depth, root, and parent already computed) and recompute its ancestors and target.
        /// </summary>
        /// <param name="comment">Comment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored comment.</returns>
        /// <exception cref="ArgumentNullException">The comment or a required field is null or empty.</exception>
        Task<Comment> CreateAsync(Comment comment, CancellationToken token = default);

        /// <summary>
        /// Read a comment in any state.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Comment, or null.</returns>
        Task<Comment?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read many comments in any state.
        /// </summary>
        /// <param name="ids">Identifiers (a bounded set).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Comments found.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        Task<List<Comment>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Write a comment's editable columns (body, excerpt, link preview, showlinkpreview, editedUtc, updatedUtc) and,
        /// when a revision is given, insert it with the next revision number and recompute revisioncount.
        /// </summary>
        /// <param name="comment">Comment with the new values.</param>
        /// <param name="revision">Previous version to keep, or null for a silent edit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored comment.</returns>
        /// <exception cref="ArgumentNullException">The comment is null.</exception>
        Task<Comment> UpdateAsync(Comment comment, CommentRevision? revision, CancellationToken token = default);

        /// <summary>
        /// Recompute attachmentcount from the attached, not deleted media rows.
        /// </summary>
        /// <param name="id">Comment identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the comment exists.</returns>
        Task<bool> RecomputeAttachmentsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Soft-delete a comment: deletedutc and deletedreason, revisions, mentions, labels, and tags deleted, then the
        /// ancestors and target recomputed. With erase (account deletion) the body, excerpt, and preview are cleared too,
        /// even when the comment was already deleted.
        /// </summary>
        /// <param name="id">Comment identifier.</param>
        /// <param name="reason">Deleted reason.</param>
        /// <param name="erase">Whether to clear the content immediately.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored comment, or null when it does not exist.</returns>
        Task<Comment?> DeleteAsync(string id, CommentDeletedReasonEnum reason, bool erase, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Set the moderation state (None, Hidden, Removed) with moderatedutc, moderatedbyuserid, and moderationreason,
        /// and recompute the ancestors and target in the same transaction.
        /// </summary>
        /// <param name="id">Comment identifier.</param>
        /// <param name="state">New state.</param>
        /// <param name="moderatorUserId">Moderator, or null for the system.</param>
        /// <param name="reason">Reason, or null.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored comment, or null when it does not exist.</returns>
        Task<Comment?> SetModerationStateAsync(string id, ModerationStateEnum state, string? moderatorUserId, string? reason, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// One page of top-level comments of a target (parentCommentId null) or of the direct replies of one comment,
        /// limited to what the viewer is shown, in the request's ordering (Top, CreatedDescending, CreatedAscending) with
        /// keyset continuation.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="parentCommentId">Parent for a children page, or null for the top level.</param>
        /// <param name="request">Normalized request (ContentCollections.CommentTree or CommentChildren).</param>
        /// <param name="viewer">Viewer filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<Comment>> EnumerateLevelAsync(TargetTypeEnum targetType, string targetId, string? parentCommentId, EnumerationRequest request, CommentViewerFilter viewer, CancellationToken token = default);

        /// <summary>
        /// The next level of a tree page in one query: for every parent, its first limitPerParent plus one replies the
        /// viewer is shown (ROW_NUMBER() OVER (PARTITION BY parentcommentid ORDER BY the ordering)), and how many such
        /// replies each parent has.
        /// </summary>
        /// <param name="parentIds">Parents (a bounded page).</param>
        /// <param name="ordering">Ordering name.</param>
        /// <param name="limitPerParent">Replies per parent to return (the query returns one more).</param>
        /// <param name="viewer">Viewer filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Level.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<CommentLevel> ListChildrenAsync(IReadOnlyCollection<string> parentIds, string ordering, int limitPerParent, CommentViewerFilter viewer, CancellationToken token = default);

        /// <summary>
        /// One page of a comment's revisions.
        /// </summary>
        /// <param name="commentId">Comment identifier.</param>
        /// <param name="request">Normalized request (ContentCollections.CommentRevisions).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<CommentRevision>> EnumerateRevisionsAsync(string commentId, EnumerationRequest request, CancellationToken token = default);

        /// <summary>
        /// Every revision of a bounded batch of comments, oldest first (account export).
        /// </summary>
        /// <param name="commentIds">Comment identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Revisions.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        Task<List<CommentRevision>> ListRevisionsAsync(IEnumerable<string> commentIds, CancellationToken token = default);

        /// <summary>
        /// A keyset batch of an author's comments by identifier (account deletion and export).
        /// </summary>
        /// <param name="authorUserId">Author.</param>
        /// <param name="afterId">Continue after this identifier, or null.</param>
        /// <param name="limit">Most comments.</param>
        /// <param name="excludeErased">Skip comments already erased for account deletion.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Comments in identifier order.</returns>
        /// <exception cref="ArgumentNullException">The author is null or empty.</exception>
        Task<List<Comment>> ListByAuthorAsync(string authorUserId, string? afterId, int limit, bool excludeErased, CancellationToken token = default);

        /// <summary>
        /// Purge a removed or deleted comment (moderation purge worker): body, excerpt, and link preview cleared,
        /// revisions, mentions, labels, and tags deleted, revisioncount reset, and purgedutc set, in one transaction.
        /// Counters are unchanged (removed and deleted comments are already excluded). Idempotent.
        /// </summary>
        /// <param name="id">Comment identifier.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call purged the comment.</returns>
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
