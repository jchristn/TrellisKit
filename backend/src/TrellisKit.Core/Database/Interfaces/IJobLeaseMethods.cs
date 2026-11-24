namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Singleton job lease data access. A lease is taken with one conditional upsert, so it works identically on every
    /// provider and with several server instances. Implementations are safe to call concurrently.
    /// </summary>
    public interface IJobLeaseMethods
    {
        /// <summary>
        /// Take or renew a lease. Succeeds when no lease exists, the existing lease lapsed, or the holder already holds it.
        /// </summary>
        /// <param name="jobName">Job name.</param>
        /// <param name="holder">Instance identifier.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="duration">Lease length. Minimum one second.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the caller now holds the lease.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> TryAcquireAsync(string jobName, string holder, DateTime nowUtc, TimeSpan duration, CancellationToken token = default);

        /// <summary>
        /// Release a lease held by the holder. Does nothing when another instance holds it.
        /// </summary>
        /// <param name="jobName">Job name.</param>
        /// <param name="holder">Instance identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task ReleaseAsync(string jobName, string holder, CancellationToken token = default);
    }
}
