namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Profile field definition data access (docs/MULTIPROPERTY_CONTRACT.md section 9). Definitions are fixed-bound
    /// configuration (at most 104 per property). Implementations are safe to call concurrently.
    /// </summary>
    public interface IProfileFieldMethods
    {
        /// <summary>
        /// Every definition of the property with options and sub-fields, in profile order (bounded: at most 200 rows).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Definitions.</returns>
        Task<List<ProfileField>> ReadAllAsync(CancellationToken token = default);

        /// <summary>
        /// Read one definition with options and sub-fields.
        /// </summary>
        /// <param name="id">Field identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Definition, or null.</returns>
        Task<ProfileField?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// One page of definitions with options and sub-fields.
        /// </summary>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="filter">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<ProfileField>> EnumerateAsync(EnumerationRequest request, ProfileFieldFilter filter, CancellationToken token = default);

        /// <summary>
        /// Create a definition with its options and sub-fields in one transaction.
        /// </summary>
        /// <param name="field">Definition (ids already assigned).</param>
        /// <param name="userId">Creating staff member, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="DuplicateRecordException">The key is in use.</exception>
        Task CreateAsync(ProfileField field, string? userId, CancellationToken token = default);

        /// <summary>
        /// Replace a definition, its options, and its sub-fields in one transaction, bumping the revision.
        /// </summary>
        /// <param name="field">Definition.</param>
        /// <param name="userId">Updating staff member, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="DuplicateRecordException">The key is in use.</exception>
        Task UpdateAsync(ProfileField field, string? userId, CancellationToken token = default);

        /// <summary>
        /// Delete a definition with its options and sub-fields (values are deleted separately).
        /// </summary>
        /// <param name="id">Field identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Set profileOrder to index times 10 for the given ids in one transaction.
        /// </summary>
        /// <param name="ids">Every field id of the property, in the new order.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task ReorderAsync(List<string> ids, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Number of non-built-in definitions.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountCustomAsync(CancellationToken token = default);

        /// <summary>
        /// A token that changes whenever any definition changes (feeds the property ETag).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Revision token.</returns>
        Task<string> RevisionAsync(CancellationToken token = default);
    }
}
