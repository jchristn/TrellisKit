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
    /// Subscription event data access. Implementations are safe to call concurrently.
    /// </summary>
    public interface ISubscriptionEventMethods
    {
        /// <summary>
        /// Insert an event unless one with the same store and store event identifier exists (webhook idempotency).
        /// </summary>
        /// <param name="subscriptionEvent">Event.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when inserted, false when it already existed.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> TryCreateAsync(SubscriptionEvent subscriptionEvent, CancellationToken token = default);

        /// <summary>
        /// Read an event by store event identifier.
        /// </summary>
        /// <param name="store">Store.</param>
        /// <param name="storeEventId">Store event identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Event, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<SubscriptionEvent?> ReadByStoreEventIdAsync(StoreEnum store, string storeEventId, CancellationToken token = default);

        /// <summary>
        /// Update an event's processing result.
        /// </summary>
        /// <param name="subscriptionEvent">Event with changed subscription, member, processed time, and outcome.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task UpdateAsync(SubscriptionEvent subscriptionEvent, CancellationToken token = default);

        /// <summary>
        /// List a subscription's events, newest first.
        /// </summary>
        /// <param name="subscriptionId">Subscription identifier.</param>
        /// <param name="limit">Maximum rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Events.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<List<SubscriptionEvent>> EnumerateBySubscriptionAsync(string subscriptionId, int limit, CancellationToken token = default);

        /// <summary>
        /// List a member's events, newest first.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="limit">Maximum rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Events.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<List<SubscriptionEvent>> EnumerateByUserAsync(string userId, int limit, CancellationToken token = default);

        /// <summary>
        /// Enumerate one subscription's events (collection <see cref="PhaseOneCollections.SubscriptionEvents"/>).
        /// </summary>
        /// <param name="subscriptionId">Subscription.</param>
        /// <param name="request">Normalized enumeration.</param>
        /// <param name="outcome">Only this outcome, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<EnumerationResult<SubscriptionEvent>> EnumerateBySubscriptionAsync(string subscriptionId, EnumerationRequest request, string? outcome, CancellationToken token = default);
    }
}
