namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Mention data access (contract section 3.3). One row per mentioned member per source (unique), with notifiedutc
    /// recording that the mention notification was handled, so a member is never notified twice for the same source
    /// and scheduled posts notify when they publish. Implementations are safe to call concurrently.
    /// </summary>
    public interface IMentionMethods
    {
        /// <summary>
        /// Resolve handles case-insensitively against active accounts (not deleted, not banned).
        /// </summary>
        /// <param name="handles">Handles without the at sign (a bounded set).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member identifiers keyed by lowercase handle.</returns>
        /// <exception cref="ArgumentNullException">The handles are null.</exception>
        Task<Dictionary<string, string>> ResolveHandlesAsync(IEnumerable<string> handles, CancellationToken token = default);

        /// <summary>
        /// Make a source's mentions exactly the given set: rows for members no longer mentioned are deleted, new members
        /// are inserted (notifiedutc null), and existing rows keep their notifiedutc.
        /// </summary>
        /// <param name="sourceType">Source type.</param>
        /// <param name="sourceId">Source identifier.</param>
        /// <param name="mentions">Mentions (distinct members).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task ReplaceAsync(TargetTypeEnum sourceType, string sourceId, IReadOnlyList<Mention> mentions, CancellationToken token = default);

        /// <summary>
        /// Claim a source's mentions that were not notified yet (sets notifiedutc in one conditional update), so only
        /// one caller notifies each.
        /// </summary>
        /// <param name="sourceType">Source type.</param>
        /// <param name="sourceId">Source identifier.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The claimed mentions.</returns>
        Task<List<Mention>> ClaimUnnotifiedAsync(TargetTypeEnum sourceType, string sourceId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Mentions of a bounded set of sources, in insertion order.
        /// </summary>
        /// <param name="sourceType">Source type.</param>
        /// <param name="sourceIds">Source identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Mentions by source identifier.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        Task<Dictionary<string, List<Mention>>> ListBySourcesAsync(TargetTypeEnum sourceType, IEnumerable<string> sourceIds, CancellationToken token = default);

        /// <summary>
        /// Delete every mention of a member (account deletion).
        /// </summary>
        /// <param name="userId">Mentioned member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteForMentionedUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Delete a source's mentions.
        /// </summary>
        /// <param name="sourceType">Source type.</param>
        /// <param name="sourceId">Source identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteBySourceAsync(TargetTypeEnum sourceType, string sourceId, CancellationToken token = default);
    }
}
