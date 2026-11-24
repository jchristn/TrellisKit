namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Member search data access: the displaynamewords child table and the indexed prefix query (contract section
    /// 3.4). Implementations are safe to call concurrently.
    /// </summary>
    public interface IMemberSearchMethods
    {
        /// <summary>
        /// Replace a member's display name words with the words of <paramref name="displayName"/> (see
        /// <see cref="TrellisKit.Core.Helpers.DisplayNameWords"/>).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="displayName">Current display name, or null to remove every word.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Words stored.</returns>
        /// <exception cref="ArgumentNullException">The member identifier is null or empty.</exception>
        Task<int> ReplaceWordsAsync(string userId, string? displayName, CancellationToken token = default);

        /// <summary>
        /// Remove a member's display name words (account deletion).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The member identifier is null or empty.</exception>
        Task DeleteWordsAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Display name words currently stored for a member.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Words (at most 100), sorted.</returns>
        Task<List<string>> WordsAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Store words for one batch of non-deleted members that have none, in identifier order after
        /// <paramref name="afterUserId"/>.
        /// </summary>
        /// <param name="afterUserId">Keyset position (last identifier of the previous batch), or null to start.</param>
        /// <param name="batchSize">Members per batch. Minimum 1, maximum 1000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifier of the last member processed, or null when no member was left.</returns>
        Task<string?> BackfillBatchAsync(string? afterUserId, int batchSize, CancellationToken token = default);

        /// <summary>
        /// Run the indexed prefix search and return member identifiers in ranked order: exact handle, then context
        /// members (the feed post's author and commenters), then handle prefix, then display name word prefix.
        /// Within the exact and handle groups members sort by lowercase handle, within the others by lowercase
        /// display name, then by identifier. Excludes the caller, deleted, banned, and age-unattested accounts,
        /// members with neither an active entitlement nor a SystemAdmin role, and both directions of the caller's blocks.
        /// The enumeration request adds the created window and label and tag filters on the members' User metadata.
        /// </summary>
        /// <param name="criteria">Search criteria.</param>
        /// <param name="request">Normalized enumeration request (collection SafetyCollections.MemberSearch).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A keyset page of member identifiers with standard totals and continuation.</returns>
        /// <exception cref="ArgumentNullException">The criteria are null.</exception>
        Task<EnumerationResult<string>> SearchAsync(MemberSearchCriteria criteria, EnumerationRequest request, CancellationToken token = default);
    }
}
