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
    /// Member directory submissions and their structured field rows (docs/PHASE4_CONTRACT.md sections 2.4 and 3.7).
    /// </summary>
    public interface IDirectorySubmissionMethods
    {
        /// <summary>
        /// Insert a submission and its field rows.
        /// </summary>
        /// <param name="submission">Submission.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored submission.</returns>
        /// <exception cref="ArgumentNullException">The submission is null.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">A pending claim by the member exists.</exception>
        Task<DirectorySubmission> CreateAsync(DirectorySubmission submission, CancellationToken token = default);

        /// <summary>
        /// Read a submission with its field rows.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Submission, or null.</returns>
        Task<DirectorySubmission?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate submissions (field rows loaded).
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="filter">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<DirectorySubmission>> EnumerateAsync(EnumerationRequest request, DirectorySubmissionFilter filter, CancellationToken token = default);

        /// <summary>
        /// Pending submissions of a member.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountPendingByUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Whether a member has a pending claim on an instructor.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="instructorId">Instructor.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when pending.</returns>
        Task<bool> HasPendingClaimAsync(string userId, string instructorId, CancellationToken token = default);

        /// <summary>
        /// Replace the proposed values of some fields with reviewer overrides, and optionally the proposed pin.
        /// </summary>
        /// <param name="id">Pending submission.</param>
        /// <param name="fields">Field rows (every field named here is replaced).</param>
        /// <param name="setGeo">Whether to replace the proposed pin.</param>
        /// <param name="lat">Latitude, or null.</param>
        /// <param name="lng">Longitude, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the submission was pending.</returns>
        /// <exception cref="ArgumentNullException">The fields are null.</exception>
        Task<bool> OverrideAsync(string id, List<DirectorySubmissionField> fields, bool setGeo, double? lat, double? lng, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Store a fresh diff: the current values of the field rows and the target's updatedutc the reviewer saw.
        /// </summary>
        /// <param name="id">Pending submission.</param>
        /// <param name="fields">Field rows with fresh CurrentValue.</param>
        /// <param name="targetUpdatedUtc">The target's updatedutc.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The fields are null.</exception>
        Task RefreshAsync(string id, List<DirectorySubmissionField> fields, DateTime? targetUpdatedUtc, CancellationToken token = default);

        /// <summary>
        /// Approve in one transaction: the submission must still be Pending and (for edits) its target unchanged since
        /// the diff was loaded; then the apply statements run and the submission is marked Approved.
        /// </summary>
        /// <param name="submission">Submission.</param>
        /// <param name="apply">Statements that apply the change.</param>
        /// <param name="targetTable">Table of the target (venues or instructors) for the stale check, or null.</param>
        /// <param name="reviewerUserId">Reviewer, or null.</param>
        /// <param name="reviewNote">Note, or null.</param>
        /// <param name="resultId">Entry created or changed, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.ApiErrorException">409 SubmissionStale or 409 NotPending.</exception>
        Task ApproveAsync(DirectorySubmission submission, List<string> apply, string? targetTable, string? reviewerUserId, string? reviewNote, string? resultId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Move a pending submission to Rejected, Withdrawn, or Superseded.
        /// </summary>
        /// <param name="id">Submission.</param>
        /// <param name="status">New status.</param>
        /// <param name="reviewerUserId">Reviewer, or null.</param>
        /// <param name="reviewNote">Note, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when it was pending.</returns>
        Task<bool> DecideAsync(string id, DirectorySubmissionStatusEnum status, string? reviewerUserId, string? reviewNote, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Pending submission counts per target.
        /// </summary>
        /// <param name="targetIds">Targets.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts by target id.</returns>
        Task<Dictionary<string, int>> PendingCountsAsync(IEnumerable<string> targetIds, CancellationToken token = default);

        /// <summary>
        /// Claim overdue pending submissions that have not been announced (overduenotifiedutc set in the claim).
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="limit">Most submissions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Submissions.</returns>
        Task<List<DirectorySubmission>> ClaimOverdueAsync(DateTime nowUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Pending submissions of the property.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountPendingAsync(CancellationToken token = default);

        /// <summary>
        /// A page of a member's submissions by identifier (account export and erasure), with field rows.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="pendingOnly">Only pending submissions.</param>
        /// <param name="afterId">Exclusive position, or null.</param>
        /// <param name="limit">Most submissions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Submissions.</returns>
        Task<List<DirectorySubmission>> ListByUserAsync(string userId, bool pendingOnly, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Anonymize a member's submissions (account deletion: the submitter becomes null).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Submissions anonymized.</returns>
        Task<int> AnonymizeAsync(string userId, DateTime nowUtc, CancellationToken token = default);
    }
}
