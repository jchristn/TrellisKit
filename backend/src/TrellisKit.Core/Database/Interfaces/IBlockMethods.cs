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
    /// Member block data access (blocks table). Implementations are safe to call concurrently.
    /// </summary>
    public interface IBlockMethods
    {
        /// <summary>
        /// Create a block unless it exists, as long as the blocker has fewer than <paramref name="maxBlocks"/> blocks.
        /// Idempotent.
        /// </summary>
        /// <param name="blockerUserId">Member who blocks.</param>
        /// <param name="blockedUserId">Member who is blocked.</param>
        /// <param name="nowUtc">Creation time for a new row.</param>
        /// <param name="maxBlocks">Most blocks a member may hold. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the block exists afterwards (created now or before); false when the limit refused it.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> CreateAsync(string blockerUserId, string blockedUserId, DateTime nowUtc, int maxBlocks, CancellationToken token = default);

        /// <summary>
        /// Remove a block. Idempotent.
        /// </summary>
        /// <param name="blockerUserId">Member who blocked.</param>
        /// <param name="blockedUserId">Member who was blocked.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row was removed.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> DeleteAsync(string blockerUserId, string blockedUserId, CancellationToken token = default);

        /// <summary>
        /// Remove every block a member made or received (account deletion).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows removed.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<int> DeleteByUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Whether the blocker blocked the other member.
        /// </summary>
        /// <param name="blockerUserId">Member who may have blocked.</param>
        /// <param name="blockedUserId">Member who may be blocked.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the block exists.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> ExistsAsync(string blockerUserId, string blockedUserId, CancellationToken token = default);

        /// <summary>
        /// Whether either member blocked the other.
        /// </summary>
        /// <param name="firstUserId">First member.</param>
        /// <param name="secondUserId">Second member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a block exists in either direction.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> EitherAsync(string firstUserId, string secondUserId, CancellationToken token = default);

        /// <summary>
        /// Number of blocks a member made.
        /// </summary>
        /// <param name="blockerUserId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<long> CountByBlockerAsync(string blockerUserId, CancellationToken token = default);

        /// <summary>
        /// One enumeration page of a member's blocks (collection SafetyCollections.Blocks).
        /// </summary>
        /// <param name="blockerUserId">Member.</param>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of blocks.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<EnumerationResult<Block>> EnumerateByBlockerAsync(string blockerUserId, EnumerationRequest request, CancellationToken token = default);

        /// <summary>
        /// Members the user blocked.
        /// </summary>
        /// <param name="blockerUserId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Blocked member identifiers.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<HashSet<string>> BlockedByAsync(string blockerUserId, CancellationToken token = default);

        /// <summary>
        /// Members who blocked the user.
        /// </summary>
        /// <param name="blockedUserId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Blocker identifiers.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<HashSet<string>> BlockersOfAsync(string blockedUserId, CancellationToken token = default);

        /// <summary>
        /// Members related to the user by a block in either direction.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member identifiers.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<HashSet<string>> RelatedAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Of the given members, those related to the user by a block in either direction.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="otherUserIds">Candidates.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The candidates that block or are blocked by the user.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<HashSet<string>> RelatedAmongAsync(string userId, IEnumerable<string> otherUserIds, CancellationToken token = default);

        /// <summary>
        /// Members the viewer blocked who do not currently hold a staff role (SystemAdmin, Moderator, FeedModerator).
        /// Content by these authors is hidden from the viewer (contract section 9.2: a blocked member who later
        /// becomes staff is shown again).
        /// </summary>
        /// <param name="viewerUserId">Viewer.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Author identifiers to hide.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<HashSet<string>> HiddenAuthorsAsync(string viewerUserId, CancellationToken token = default);
    }
}
