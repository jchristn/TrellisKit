namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The content changelog (docs/INTEGRATION.md content review contract). Rows are written only by the database triggers
    /// on the member-content tables, in the same transaction as the change; these methods read them and record their
    /// review. Thread safety: safe for concurrent use.
    /// </summary>
    public interface IContentChangeMethods
    {
        /// <summary>
        /// Enumerate rows (the admin queue).
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<ContentChangeRecord>> EnumerateAsync(EnumerationRequest request, ContentChangeFilter filter, CancellationToken token = default);

        /// <summary>
        /// Read one row.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Row, or null.</returns>
        Task<ContentChangeRecord?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// The oldest Pending rows.
        /// </summary>
        /// <param name="max">Most rows (1 to 5000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows, oldest first.</returns>
        Task<List<ContentChangeRecord>> ListPendingAsync(int max, CancellationToken token = default);

        /// <summary>
        /// Record the reviewer's verdict on a Pending row.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="status">Clean or Flagged.</param>
        /// <param name="categories">Categories (empty when Clean).</param>
        /// <param name="severity">Severity.</param>
        /// <param name="note">Reviewer note, or null.</param>
        /// <param name="reviewedBy">Reviewer ("model:..." or "system").</param>
        /// <param name="kind">Reviewer kind.</param>
        /// <param name="nowUtc">Review time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the row was Pending and is now reviewed.</returns>
        /// <exception cref="ArgumentNullException">categories is null.</exception>
        Task<bool> CompleteReviewAsync(string id, ContentReviewStatusEnum status, List<ContentReviewCategoryEnum> categories, ContentReviewSeverityEnum severity, string? note,
            string reviewedBy, ContentReviewerKindEnum kind, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Count a failed review attempt on a Pending row.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="error">Failure (at most 500 characters stored).</param>
        /// <param name="nowUtc">Time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Failed attempts so far, or 0 when the row is no longer Pending.</returns>
        Task<int> RecordFailureAsync(string id, string error, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Record an administrator's decision (Dismissed or Actioned) on any row.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="status">Dismissed or Actioned.</param>
        /// <param name="note">Note, or null (kept as the reviewer note when present).</param>
        /// <param name="reviewedBy">Administrator ("user:..." or "admin-token").</param>
        /// <param name="moderationActionId">Moderation action taken, or null.</param>
        /// <param name="nowUtc">Time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the row exists.</returns>
        Task<bool> ResolveAsync(string id, ContentReviewStatusEnum status, string? note, string reviewedBy, string? moderationActionId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Mark that administrators were notified about a flagged row (once).
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="nowUtc">Time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call marked it (it was not marked before).</returns>
        Task<bool> MarkNotifiedAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Queue counts.
        /// </summary>
        /// <param name="flaggedSinceUtc">Start of the FlaggedSince window.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts.</returns>
        Task<ContentReviewCounts> CountsAsync(DateTime flaggedSinceUtc, CancellationToken token = default);

        /// <summary>
        /// Delete reviewed rows (Clean, Dismissed, Actioned) written before a time.
        /// </summary>
        /// <param name="beforeUtc">Cutoff.</param>
        /// <param name="max">Most rows (1 to 50000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> PruneAsync(DateTime beforeUtc, int max, CancellationToken token = default);

        /// <summary>
        /// Account deletion: clear the text and media of every row the member authored (the rows stay as the review
        /// history), and dismiss the ones still Pending.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows cleared.</returns>
        /// <exception cref="ArgumentNullException">userId is null or empty.</exception>
        Task<int> EraseForUserAsync(string userId, DateTime nowUtc, CancellationToken token = default);
    }
}
