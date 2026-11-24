namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Notification fan-out job (notificationfanouts) data access. Jobs are unique per type and source and claimed
    /// with a conditional update. Implementations are safe to call concurrently.
    /// </summary>
    public interface INotificationFanoutMethods
    {
        /// <summary>
        /// Insert a job unless one exists for the same type and source.
        /// </summary>
        /// <param name="job">Job.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call created it.</returns>
        /// <exception cref="ArgumentNullException">The job is null.</exception>
        Task<bool> CreateAsync(NotificationFanout job, CancellationToken token = default);

        /// <summary>
        /// Read the job of a type and source.
        /// </summary>
        /// <param name="type">Type.</param>
        /// <param name="sourceId">Source.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Job, or null.</returns>
        /// <exception cref="ArgumentNullException">The source is null or empty.</exception>
        Task<NotificationFanout?> ReadAsync(NotificationTypeEnum type, string sourceId, CancellationToken token = default);

        /// <summary>
        /// Claim up to a number of unfinished jobs whose claim is free or lapsed, oldest first.
        /// </summary>
        /// <param name="holder">Worker instance.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="claimDuration">Claim length.</param>
        /// <param name="limit">Maximum jobs.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Claimed jobs.</returns>
        /// <exception cref="ArgumentNullException">The holder is null or empty.</exception>
        Task<List<NotificationFanout>> ClaimAsync(string holder, DateTime nowUtc, TimeSpan claimDuration, int limit, CancellationToken token = default);

        /// <summary>
        /// Record a finished batch: the keyset position, notifications added (incremented in SQL), and a renewed claim.
        /// </summary>
        /// <param name="id">Job.</param>
        /// <param name="holder">Worker instance (must hold the claim).</param>
        /// <param name="lastUserId">Last recipient processed.</param>
        /// <param name="added">Notifications created by the batch.</param>
        /// <param name="claimUntilUtc">New claim expiry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the holder still held the claim.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> RecordProgressAsync(string id, string holder, string lastUserId, int added, DateTime claimUntilUtc, CancellationToken token = default);

        /// <summary>
        /// Mark a job Done and release its claim.
        /// </summary>
        /// <param name="id">Job.</param>
        /// <param name="holder">Worker instance (must hold the claim).</param>
        /// <param name="nowUtc">Completion time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the holder still held the claim.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> CompleteAsync(string id, string holder, DateTime nowUtc, CancellationToken token = default);
    }
}
