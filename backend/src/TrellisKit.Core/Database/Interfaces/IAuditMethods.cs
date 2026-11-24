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
    /// Audit record data access. Implementations are safe to call concurrently.
    /// </summary>
    public interface IAuditMethods
    {
        /// <summary>
        /// Insert an audit record.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<AuditRecord> CreateAsync(AuditRecord record, CancellationToken token = default);

        /// <summary>
        /// Enumerate audit records (collection <see cref="PhaseOneCollections.Audit"/>).
        /// </summary>
        /// <param name="request">Normalized enumeration.</param>
        /// <param name="filter">Typed filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<EnumerationResult<AuditRecord>> EnumerateAsync(EnumerationRequest request, AuditFilter filter, CancellationToken token = default);

        /// <summary>
        /// List records where the member is the actor or the target, newest first.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="limit">Maximum rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Records.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<List<AuditRecord>> EnumerateByUserAsync(string userId, int limit, CancellationToken token = default);

        /// <summary>
        /// One keyset batch of a member's audit records (as actor or target), ordered by id (identifiers are K-sortable), for streaming exports.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="afterId">Return records with an id after this one, or null for the first batch.</param>
        /// <param name="limit">Batch size, 1 to 1000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Up to limit records.</returns>
        /// <exception cref="ArgumentNullException">The member identifier is null or empty.</exception>
        Task<List<AuditRecord>> EnumerateByUserAfterAsync(string userId, string? afterId, int limit, CancellationToken token = default);
    }
}
