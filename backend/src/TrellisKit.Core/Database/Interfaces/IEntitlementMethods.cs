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
    /// Membership entitlement data access. Implementations are safe to call concurrently.
    /// </summary>
    public interface IEntitlementMethods
    {
        /// <summary>
        /// Insert an entitlement.
        /// </summary>
        /// <param name="entitlement">Entitlement.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entitlement.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<Entitlement> CreateAsync(Entitlement entitlement, CancellationToken token = default);

        /// <summary>
        /// Read an entitlement.
        /// </summary>
        /// <param name="id">Entitlement identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entitlement, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<Entitlement?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read the entitlement of a source.
        /// </summary>
        /// <param name="source">Source.</param>
        /// <param name="sourceId">Source identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entitlement, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<Entitlement?> ReadBySourceAsync(MembershipSourceEnum source, string sourceId, CancellationToken token = default);

        /// <summary>
        /// List a member's entitlements, newest first.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entitlements.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<List<Entitlement>> EnumerateByUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// One keyset batch of a member's entitlements, ordered by id (identifiers are K-sortable), for streaming exports.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="afterId">Return records with an id after this one, or null for the first batch.</param>
        /// <param name="limit">Batch size, 1 to 1000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Up to limit records.</returns>
        /// <exception cref="ArgumentNullException">The member identifier is null or empty.</exception>
        Task<List<Entitlement>> EnumerateByUserAfterAsync(string userId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// List entitlements of several members.
        /// </summary>
        /// <param name="userIds">Member identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entitlements.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<List<Entitlement>> EnumerateByUsersAsync(IEnumerable<string> userIds, CancellationToken token = default);

        /// <summary>
        /// Update an entitlement.
        /// </summary>
        /// <param name="entitlement">Entitlement with changed values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entitlement.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<Entitlement> UpdateAsync(Entitlement entitlement, CancellationToken token = default);

        /// <summary>
        /// Count non-deleted members with an active entitlement.
        /// </summary>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="source">Only this source, or null for any.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountActiveUsersAsync(DateTime nowUtc, MembershipSourceEnum? source, CancellationToken token = default);

        /// <summary>
        /// Count non-deleted members who are active only through codes or comps.
        /// </summary>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountFreeOnlyUsersAsync(DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Count non-deleted members with an active subscription entitlement, by store.
        /// </summary>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts for every store.</returns>
        Task<Dictionary<StoreEnum, long>> CountActiveUsersByStoreAsync(DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Count non-deleted members whose entitlement ended in the window and who have no active entitlement now.
        /// </summary>
        /// <param name="fromUtc">Window start.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountLapsedUsersAsync(DateTime fromUtc, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate one member's entitlements (collections <see cref="PhaseOneCollections.AdminEntitlements"/> and
        /// <see cref="PhaseOneCollections.MembershipHistory"/>).
        /// </summary>
        /// <param name="request">Normalized enumeration.</param>
        /// <param name="filter">Typed filters; UserId is required.</param>
        /// <param name="nowUtc">Current time, used by the status filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null, or the user id is empty.</exception>
        Task<EnumerationResult<Entitlement>> EnumerateAsync(EnumerationRequest request, EntitlementFilter filter, DateTime nowUtc, CancellationToken token = default);
    }
}
