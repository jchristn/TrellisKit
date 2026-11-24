namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Rank history entry data access. Implementations are safe to call concurrently.
    /// </summary>
    public interface IRankHistoryMethods
    {
        /// <summary>
        /// List a member's rank history in display order.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<List<RankHistoryEntry>> EnumerateByUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Replace a member's rank history in one transaction.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="entries">New entries in display order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task ReplaceForUserAsync(string userId, List<RankHistoryEntry> entries, CancellationToken token = default);
    }
}
