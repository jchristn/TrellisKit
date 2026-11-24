namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Realtime connection registry data access (docs/PHASE5_CONTRACT.md section 4.11, table realtimeconnections).
    /// Every method is property-bound except <see cref="DeleteForTaskAsync"/> and <see cref="PruneStaleAsync"/>, which are
    /// deployment-wide statements on the reviewed allow-list (docs/MULTIPROPERTY_CONTRACT.md section 4.3 rule 1) and must
    /// be called on the deployment driver. Implementations are safe to call concurrently.
    /// </summary>
    public interface IRealtimeConnectionMethods
    {
        /// <summary>
        /// Insert or replace a connection row (hello and state frames).
        /// </summary>
        /// <param name="record">Row.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The row, its id, user, session, or task is null or empty.</exception>
        Task UpsertAsync(RealtimeConnectionRecord record, CancellationToken token = default);

        /// <summary>
        /// Set lastseenutc on connection rows (the 60 second touch), in batches of at most 500 ids.
        /// </summary>
        /// <param name="ids">Connection ids.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows touched.</returns>
        /// <exception cref="ArgumentNullException">The ids are null.</exception>
        Task<int> TouchAsync(IEnumerable<string> ids, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Delete one connection row (disconnect).
        /// </summary>
        /// <param name="id">Connection id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row was deleted.</returns>
        /// <exception cref="ArgumentNullException">The id is null or empty.</exception>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// A member's connection rows seen at or after a time, newest first, at most 100 (bounded by
        /// realtime.maxConnectionsPerUser per task).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="seenSinceUtc">Oldest lastseenutc to include.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task<List<RealtimeConnectionRecord>> EnumerateRecentByUserAsync(string userId, DateTime seenSinceUtc, CancellationToken token = default);

        /// <summary>
        /// Deployment-wide (reviewed allow-list): delete every row of one server task (draining and startup cleanup).
        /// Call on the deployment driver.
        /// </summary>
        /// <param name="taskId">Task id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        /// <exception cref="ArgumentNullException">The task id is null or empty.</exception>
        Task<int> DeleteForTaskAsync(string taskId, CancellationToken token = default);

        /// <summary>
        /// Deployment-wide (reviewed allow-list): delete at most <paramref name="limit"/> rows not seen since a time
        /// (RealtimeRegistryPruneWorker). Call on the deployment driver.
        /// </summary>
        /// <param name="seenBeforeUtc">Rows with lastseenutc before this are deleted.</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> PruneStaleAsync(DateTime seenBeforeUtc, int limit, CancellationToken token = default);
    }
}
