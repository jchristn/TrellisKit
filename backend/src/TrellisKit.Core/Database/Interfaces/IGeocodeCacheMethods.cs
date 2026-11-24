namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The per-property geocoder cache, keyed by provider and the SHA-256 of the normalized query.
    /// </summary>
    public interface IGeocodeCacheMethods
    {
        /// <summary>
        /// Read an unexpired entry.
        /// </summary>
        /// <param name="provider">Provider name.</param>
        /// <param name="queryHash">Query hash.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entry, or null.</returns>
        Task<GeocodeCacheEntry?> ReadAsync(string provider, string queryHash, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Insert or replace an entry.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The entry is null.</exception>
        Task WriteAsync(GeocodeCacheEntry entry, CancellationToken token = default);

        /// <summary>
        /// Delete expired entries.
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="limit">Most entries per call.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries deleted.</returns>
        Task<int> PruneAsync(DateTime nowUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Entries of the property (tests and metrics).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountAsync(CancellationToken token = default);
    }
}
