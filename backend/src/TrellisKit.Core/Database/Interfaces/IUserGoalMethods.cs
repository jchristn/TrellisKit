namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Member goal data access. Implementations are safe to call concurrently.
    /// </summary>
    public interface IUserGoalMethods
    {
        /// <summary>
        /// List a member's goals in display order.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Goals.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<List<string>> EnumerateByUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Replace a member's goals in one transaction.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="goals">New goals in display order (duplicates are ignored).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task ReplaceForUserAsync(string userId, List<string> goals, CancellationToken token = default);
    }
}
