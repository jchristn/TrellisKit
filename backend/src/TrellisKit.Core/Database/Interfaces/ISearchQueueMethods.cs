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
    /// Search index queue methods (resolved decision 13: a background indexer keeps the search index current).
    /// </summary>
    public interface ISearchQueueMethods
    {
        /// <summary>
        /// Queue an object for search indexing (re-queues an existing entry).
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target id.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        Task EnqueueAsync(TargetTypeEnum targetType, string targetId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// The oldest queued entries with fewer than maxAttempts attempts.
        /// </summary>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="maxAttempts">Max attempts.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<SearchQueueItem>> PeekAsync(int limit, int maxAttempts, CancellationToken token = default);

        /// <summary>
        /// Remove an entry unless it was re-queued meanwhile.
        /// </summary>
        /// <param name="item">Item.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> CompleteAsync(SearchQueueItem item, CancellationToken token = default);

        /// <summary>
        /// Count a failed attempt.
        /// </summary>
        /// <param name="item">Item.</param>
        /// <param name="token">Cancellation token.</param>
        Task FailAsync(SearchQueueItem item, CancellationToken token = default);

        /// <summary>
        /// Queued entries.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<long> CountAsync(CancellationToken token = default);

        /// <summary>
        /// Queue every indexable object (rebuild).
        /// </summary>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<int> EnqueueAllAsync(DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// The time of the last change sweep, or null before the first sweep.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<DateTime?> ReadWatermarkAsync(CancellationToken token = default);

        /// <summary>
        /// Store the time of the last change sweep.
        /// </summary>
        /// <param name="watermarkUtc">Watermark utc.</param>
        /// <param name="token">Cancellation token.</param>
        Task WriteWatermarkAsync(DateTime watermarkUtc, CancellationToken token = default);

        /// <summary>
        /// Queue every indexable object updated since a time (the change sweep).
        /// </summary>
        /// <param name="sinceUtc">Since utc.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<int> EnqueueChangedAsync(DateTime sinceUtc, DateTime nowUtc, CancellationToken token = default);
    }
}
