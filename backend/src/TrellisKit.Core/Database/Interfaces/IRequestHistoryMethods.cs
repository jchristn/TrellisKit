namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Request history data access methods. Implementations are safe to call concurrently.
    /// </summary>
    public interface IRequestHistoryMethods
    {
        /// <summary>
        /// Insert a captured request.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The entry is null.</exception>
        Task CreateAsync(RequestHistoryEntry entry, CancellationToken token = default);

        /// <summary>
        /// Read an entry by identifier, including headers and bodies.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entry, or null if not found.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task<RequestHistoryEntry?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate entries (collection <see cref="PhaseOneCollections.RequestHistory"/>). Items exclude headers and bodies.
        /// </summary>
        /// <param name="request">Normalized enumeration.</param>
        /// <param name="filter">Typed filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The filter is null.</exception>
        Task<EnumerationResult<RequestHistoryEntry>> EnumerateAsync(EnumerationRequest request, RequestHistoryFilter filter, CancellationToken token = default);

        /// <summary>
        /// Summarize entries into time buckets. Every bucket between FromUtc and ToUtc is returned.
        /// </summary>
        /// <param name="filter">Filter; FromUtc and ToUtc are required.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        /// <exception cref="ArgumentNullException">The filter is null.</exception>
        /// <exception cref="ArgumentException">FromUtc or ToUtc is missing, or the range is invalid or too large.</exception>
        Task<RequestHistorySummary> SummarizeAsync(RequestHistoryFilter filter, CancellationToken token = default);

        /// <summary>
        /// Delete an entry.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if an entry was deleted.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Delete all entries matching a filter. Paging fields are ignored.
        /// </summary>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        /// <exception cref="ArgumentNullException">The filter is null.</exception>
        Task<int> DeleteManyAsync(RequestHistoryFilter filter, CancellationToken token = default);

        /// <summary>
        /// Delete entries created before a cutoff.
        /// </summary>
        /// <param name="olderThanUtc">Cutoff (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken token = default);

        /// <summary>
        /// Count captured requests since a time, optionally only those with a status code at or above a value.
        /// </summary>
        /// <param name="sinceUtc">Earliest creation time.</param>
        /// <param name="minStatusCode">Lowest status code, or null for all.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountSinceAsync(DateTime sinceUtc, int? minStatusCode, CancellationToken token = default);
    }
}
