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
    /// Member account data access. Implementations are safe to call concurrently.
    /// </summary>
    public interface IUserMethods
    {
        /// <summary>
        /// Insert a member.
        /// </summary>
        /// <param name="user">User to insert.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The inserted member.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">A unique value is already in use.</exception>
        Task<User> CreateAsync(User user, CancellationToken token = default);

        /// <summary>
        /// Read a member by identifier.
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<User?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read several members by identifier. Unknown identifiers are skipped.
        /// </summary>
        /// <param name="ids">Member identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Members found.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<List<User>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Read a member by email.
        /// </summary>
        /// <param name="email">Email address, compared case-insensitively.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<User?> ReadByEmailAsync(string email, CancellationToken token = default);

        /// <summary>
        /// Read a member by handle.
        /// </summary>
        /// <param name="handle">Handle, compared case-insensitively.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<User?> ReadByHandleAsync(string handle, CancellationToken token = default);

        /// <summary>
        /// Read a member by store account token.
        /// </summary>
        /// <param name="storeAccountToken">Store account token (UUID).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<User?> ReadByStoreAccountTokenAsync(string storeAccountToken, CancellationToken token = default);

        /// <summary>
        /// Read a member by Stripe customer identifier.
        /// </summary>
        /// <param name="customerId">Stripe customer identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<User?> ReadByStripeCustomerIdAsync(string customerId, CancellationToken token = default);

        /// <summary>
        /// Update every mutable column of a member.
        /// </summary>
        /// <param name="user">Member with changed values; UpdatedUtc is set by the caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The member.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">A unique value is already in use.</exception>
        Task<User> UpdateAsync(User user, CancellationToken token = default);

        /// <summary>
        /// Record authenticated activity.
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="seenUtc">Time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task TouchLastSeenAsync(string id, DateTime seenUtc, CancellationToken token = default);

        /// <summary>
        /// Set or clear a member's palette preset preference (users.palettepreset; docs/TRELLISKIT.md section 2.1). Only
        /// this column changes; <see cref="UpdateAsync"/> never writes it.
        /// </summary>
        /// <param name="id">Member id.</param>
        /// <param name="presetId">Engine preset id, or null for the property's brand palette.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentException">The id is empty.</exception>
        Task SetPalettePresetAsync(string id, string? presetId, CancellationToken token = default);

        /// <summary>
        /// Enumerate members (collection <see cref="PhaseOneCollections.AdminMembers"/>).
        /// </summary>
        /// <param name="request">Normalized enumeration.</param>
        /// <param name="filter">Typed filters.</param>
        /// <param name="nowUtc">Current time, used by the membership filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<User>> EnumerateAsync(EnumerationRequest request, UserFilter filter, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Count members that are not deleted.
        /// </summary>
        /// <param name="createdSinceUtc">Only members created at or after this time, or null for all.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountAsync(DateTime? createdSinceUtc, CancellationToken token = default);

        /// <summary>
        /// Count non-deleted members seen since a time.
        /// </summary>
        /// <param name="sinceUtc">Earliest last-seen time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountSeenSinceAsync(DateTime sinceUtc, CancellationToken token = default);
    }
}
