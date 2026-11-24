namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Push token (pushtokens) and push delivery (pushdeliveries) data access. Deleting a token always deletes its
    /// deliveries first. Session revocation deletes tokens centrally in the session methods. Implementations are safe to
    /// call concurrently.
    /// </summary>
    public interface IPushTokenMethods
    {
        /// <summary>
        /// Register a token: insert it, or when the token value exists, move it to this row's member and session and
        /// refresh its device fields (fields the request omits, null here, keep their stored values) and lastSeenUtc
        /// (failure state reset).
        /// </summary>
        /// <param name="pushToken">Token (Id set by the caller for the insert case).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored row; its Id differs from the argument's when the token already existed.</returns>
        /// <exception cref="ArgumentNullException">The token is null.</exception>
        Task<PushToken> UpsertAsync(PushToken pushToken, CancellationToken token = default);

        /// <summary>
        /// Keep a member's most recently seen tokens and delete the rest.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="keep">Tokens to keep.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tokens deleted.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task<int> TrimAsync(string userId, int keep, CancellationToken token = default);

        /// <summary>
        /// Delete a member's token by value.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="tokenValue">Token value.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a token was deleted.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> DeleteByValueAsync(string userId, string tokenValue, CancellationToken token = default);

        /// <summary>
        /// Delete a token by identifier.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when it existed.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Delete every token of a member.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tokens deleted.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task<int> DeleteByUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// A member's tokens, most recently seen first.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tokens.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task<List<PushToken>> EnumerateByUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Enumerate a member's tokens (collection NotificationCollections.PushTokens).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="request">Prepared enumeration request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The member or request is null.</exception>
        Task<EnumerationResult<PushToken>> EnumerateForUserAsync(string userId, EnumerationRequest request, CancellationToken token = default);

        /// <summary>
        /// Tokens that can receive pushes for several members: not disabled, and bound to a session that is neither
        /// revoked nor expired.
        /// </summary>
        /// <param name="userIds">Members.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tokens.</returns>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        Task<List<PushToken>> EnumerateActiveByUsersAsync(IEnumerable<string> userIds, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Record a successful delivery: reset the failure count and set lastSuccessUtc.
        /// </summary>
        /// <param name="id">Token identifier.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task RecordSuccessAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Record a failed delivery (count incremented in SQL) and disable the token when the count reaches a limit.
        /// </summary>
        /// <param name="id">Token identifier.</param>
        /// <param name="disableAfter">Consecutive failures that disable the token.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task RecordFailureAsync(string id, int disableAfter, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Read a token.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Token, or null.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task<PushToken?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Insert delivery rows in one transaction.
        /// </summary>
        /// <param name="deliveries">Deliveries.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        Task CreateDeliveriesAsync(List<PushDelivery> deliveries, CancellationToken token = default);

        /// <summary>
        /// Sent deliveries with a ticket whose receipt is due (created at or before a time and not yet checked).
        /// </summary>
        /// <param name="createdBeforeUtc">Only deliveries created at or before this time.</param>
        /// <param name="limit">Maximum rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Deliveries, oldest first.</returns>
        Task<List<PushDelivery>> EnumerateDueReceiptsAsync(DateTime createdBeforeUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Update a delivery's status, error, and receipt time.
        /// </summary>
        /// <param name="delivery">Delivery.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The delivery is null.</exception>
        Task UpdateDeliveryAsync(PushDelivery delivery, CancellationToken token = default);

        /// <summary>
        /// The deliveries of a notification.
        /// </summary>
        /// <param name="notificationId">Notification.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Deliveries.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task<List<PushDelivery>> EnumerateDeliveriesAsync(string notificationId, CancellationToken token = default);

        /// <summary>
        /// Delete deliveries created before a time.
        /// </summary>
        /// <param name="createdBeforeUtc">Cutoff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> PruneDeliveriesAsync(DateTime createdBeforeUtc, CancellationToken token = default);
    }
}
