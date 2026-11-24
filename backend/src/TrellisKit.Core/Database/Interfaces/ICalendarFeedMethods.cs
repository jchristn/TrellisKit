namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Personal calendar feeds (docs/PHASE4_CONTRACT.md section 4.5): one per member, secret stored hashed. Every
    /// statement is property scoped.
    /// </summary>
    public interface ICalendarFeedMethods
    {
        /// <summary>
        /// A member's feed, or null.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Feed or null.</returns>
        Task<CalendarFeed?> ReadForUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// A feed by id, or null.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Feed or null.</returns>
        Task<CalendarFeed?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Create a member's feed, or replace its id and secret (rotation); the feed is enabled afterwards.
        /// </summary>
        /// <param name="feed">Feed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored feed.</returns>
        Task<CalendarFeed> UpsertAsync(CalendarFeed feed, CancellationToken token = default);

        /// <summary>
        /// Delete a member's feed.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> DeleteForUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Disable a member's feed (ban, deletion, lost access); a later rotation enables it again.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a feed was disabled.</returns>
        Task<bool> DisableForUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Record a fetch.
        /// </summary>
        /// <param name="id">Feed.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task TouchAsync(string id, DateTime nowUtc, CancellationToken token = default);
    }
}
