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
    /// Property-scoped profile gallery items. Upload attachment and item creation are atomic.
    /// </summary>
    public interface IProfileMediaMethods
    {
        /// <summary>
        /// Store new items of one profile.
        /// </summary>
        /// <param name="items">Items (same profile).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The items are null.</exception>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">A media id is already an item.</exception>
        Task CreateManyAsync(List<ProfileMediaItem> items, CancellationToken token = default);

        /// <summary>
        /// Read an item in any state, or null.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Item or null.</returns>
        Task<ProfileMediaItem?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read items by identifier.
        /// </summary>
        /// <param name="ids">Identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Items.</returns>
        Task<List<ProfileMediaItem>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Soft delete an item (the uploader deleted it).
        /// </summary>
        /// <param name="id">Item.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> SoftDeleteAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Set the moderation state of an item.
        /// </summary>
        /// <param name="id">Item.</param>
        /// <param name="state">State.</param>
        /// <param name="byUserId">Moderator, or null.</param>
        /// <param name="reason">Reason, or null.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when updated.</returns>
        Task<bool> SetModerationAsync(string id, ModerationStateEnum state, string? byUserId, string? reason, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Store the open report count of an item.
        /// </summary>
        /// <param name="id">Item.</param>
        /// <param name="count">Open reports.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task SetOpenReportCountAsync(string id, int count, CancellationToken token = default);

        /// <summary>
        /// Enumerate the items of a profile (orderings CreatedDescending, CreatedAscending).
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<ProfileMediaItem>> EnumerateAsync(EnumerationRequest request, ProfileMediaFilter filter, CancellationToken token = default);

        /// <summary>
        /// Account deletion: soft delete up to limit live items by the member, preserving the moderation record.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="limit">Most items.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The deleted items.</returns>
        Task<List<ProfileMediaItem>> EraseForUserAsync(string userId, DateTime nowUtc, int limit, CancellationToken token = default);
        /// <summary>Read an account export page in identifier order.</summary>
        Task<List<ProfileMediaItem>> ReadForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default);
    }
}
