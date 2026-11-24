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
    /// Store subscription data access. Implementations are safe to call concurrently.
    /// </summary>
    public interface ISubscriptionMethods
    {
        /// <summary>
        /// Insert a subscription.
        /// </summary>
        /// <param name="subscription">Subscription.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The subscription.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">A unique value is already in use.</exception>
        Task<Subscription> CreateAsync(Subscription subscription, CancellationToken token = default);

        /// <summary>
        /// Read a subscription.
        /// </summary>
        /// <param name="id">Subscription identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Subscription, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<Subscription?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a subscription by store identifier.
        /// </summary>
        /// <param name="store">Store.</param>
        /// <param name="storeSubscriptionId">Store subscription identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Subscription, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<Subscription?> ReadByStoreIdAsync(StoreEnum store, string storeSubscriptionId, CancellationToken token = default);

        /// <summary>
        /// List a member's subscriptions, newest first.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Subscriptions.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<List<Subscription>> EnumerateByUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// One keyset batch of a member's subscriptions, ordered by id (identifiers are K-sortable), for streaming exports.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="afterId">Return records with an id after this one, or null for the first batch.</param>
        /// <param name="limit">Batch size, 1 to 1000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Up to limit records.</returns>
        /// <exception cref="ArgumentNullException">The member identifier is null or empty.</exception>
        Task<List<Subscription>> EnumerateByUserAfterAsync(string userId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Update a subscription.
        /// </summary>
        /// <param name="subscription">Subscription with changed values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The subscription.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<Subscription> UpdateAsync(Subscription subscription, CancellationToken token = default);

        /// <summary>
        /// Enumerate subscriptions (collection <see cref="PhaseOneCollections.Subscriptions"/>).
        /// </summary>
        /// <param name="request">Normalized enumeration.</param>
        /// <param name="filter">Typed filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<EnumerationResult<Subscription>> EnumerateAsync(EnumerationRequest request, SubscriptionFilter filter, CancellationToken token = default);

        /// <summary>
        /// List subscriptions that are not Expired, Revoked, or Refunded, least recently checked first.
        /// </summary>
        /// <param name="limit">Maximum rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Subscriptions.</returns>
        Task<List<Subscription>> EnumerateForReconciliationAsync(int limit, CancellationToken token = default);
    }
}
