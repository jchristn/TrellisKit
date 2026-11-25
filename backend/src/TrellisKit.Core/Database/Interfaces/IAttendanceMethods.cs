namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Attendance data access (table attendance). Implementations are bound to one property and safe to call concurrently.
    /// </summary>
    public interface IAttendanceMethods
    {
        /// <summary>
        /// Insert a check-in.
        /// </summary>
        /// <param name="record">Check-in.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The record is null.</exception>
        /// <exception cref="DuplicateRecordException">The member already checked in to this target and date.</exception>
        Task CreateAsync(AttendanceView record, CancellationToken token = default);

        /// <summary>
        /// Read one check-in.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Check-in, or null.</returns>
        Task<AttendanceView?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Delete one check-in with its labels and tags.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when it existed.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate check-ins.
        /// </summary>
        /// <param name="request">Normalized request (AttendanceCollections).</param>
        /// <param name="filter">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<AttendanceView>> EnumerateAsync(EnumerationRequest request, AttendanceFilter filter, CancellationToken token = default);

        /// <summary>
        /// Count a member's check-ins, optionally since a time.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="sinceUtc">Only check-ins at or after this time, or null for all.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task<long> CountForUserAsync(string userId, DateTime? sinceUtc, CancellationToken token = default);

        /// <summary>
        /// A member's check-ins after an identifier (account export).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="afterId">Identifier position, or null.</param>
        /// <param name="limit">Maximum rows (1 to 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows in identifier order.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task<List<AttendanceView>> ListForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Delete a member's check-ins with their labels and tags (account deletion).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task EraseForUserAsync(string userId, CancellationToken token = default);
    }
}
