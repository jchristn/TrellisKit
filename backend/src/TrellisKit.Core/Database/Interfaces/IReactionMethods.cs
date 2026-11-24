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
    /// Reaction and reaction count data access. Every write locks the target row first, then changes the reaction rows,
    /// then recomputes reactioncounts for the affected keys and the target's reactiontotal (and, for comments,
    /// score = reactiontotal + 2 * childcount) from the rows in the same transaction, so concurrent writes never drift a
    /// count and retries are idempotent. Implementations are safe to call concurrently.
    /// </summary>
    public interface IReactionMethods
    {
        /// <summary>
        /// Add a member's reaction key to a target. Idempotent.
        /// </summary>
        /// <param name="targetType">FeedPost or Comment.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="userId">Member.</param>
        /// <param name="key">Reaction key.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call added the row (false when it already existed).</returns>
        /// <exception cref="ArgumentNullException">An identifier or the key is null or empty.</exception>
        Task<bool> AddAsync(TargetTypeEnum targetType, string targetId, string userId, string key, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Remove a member's reaction key from a target. Idempotent.
        /// </summary>
        /// <param name="targetType">FeedPost or Comment.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="userId">Member.</param>
        /// <param name="key">Reaction key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An identifier or the key is null or empty.</exception>
        Task RemoveAsync(TargetTypeEnum targetType, string targetId, string userId, string key, CancellationToken token = default);

        /// <summary>
        /// Remove every reaction of a member on one target (account deletion), recomputing every key's count, and delete
        /// the reactions' labels and tags.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task RemoveAllForUserOnTargetAsync(TargetTypeEnum targetType, string targetId, string userId, CancellationToken token = default);

        /// <summary>
        /// Stored counts above zero per key for a bounded set of targets (every stored key; callers drop keys that are
        /// not in the property's current set).
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetIds">Target identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts by target id, then by key.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        Task<Dictionary<string, Dictionary<string, long>>> CountsAsync(TargetTypeEnum targetType, IEnumerable<string> targetIds, CancellationToken token = default);

        /// <summary>
        /// A member's keys on a bounded set of targets.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetIds">Target identifiers.</param>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Keys by target id.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        Task<Dictionary<string, List<string>>> MineAsync(TargetTypeEnum targetType, IEnumerable<string> targetIds, string userId, CancellationToken token = default);

        /// <summary>
        /// One page of who reacted to a target.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="request">Normalized request (ContentCollections.Reactions).</param>
        /// <param name="key">Only this key, or null for all keys.</param>
        /// <param name="allowedKeys">Keys in the property's current set (rows of removed keys are hidden).</param>
        /// <param name="excludedUserIds">Members to omit (the caller blocked them).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<Reaction>> EnumerateAsync(TargetTypeEnum targetType, string targetId, EnumerationRequest request, string? key, IReadOnlyCollection<string> allowedKeys, IReadOnlyCollection<string> excludedUserIds, CancellationToken token = default);

        /// <summary>
        /// A keyset batch of a member's reactions by identifier (account deletion and export).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="afterId">Continue after this identifier, or null.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Reactions in identifier order.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task<List<Reaction>> ListByUserAsync(string userId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Reaction rows stored for a target (every key), for integrity checks.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        Task<long> CountRowsAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default);
    }
}
