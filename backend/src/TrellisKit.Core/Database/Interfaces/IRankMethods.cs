namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Verified rank data access (tables memberranks, rankpromotions, rankrequirements, lineageentries). Implementations
    /// are bound to one property and safe to call concurrently.
    /// </summary>
    public interface IRankMethods
    {
        /// <summary>
        /// A member's rank rows (one per Rank field; at most 50).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task<List<MemberRank>> ReadMemberRanksAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// The rank rows of many members in one field.
        /// </summary>
        /// <param name="fieldKey">Rank field key (case-insensitive).</param>
        /// <param name="userIds">Members (at most 1000 distinct).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows keyed by member.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<Dictionary<string, MemberRank>> ReadMemberRanksAsync(string fieldKey, IEnumerable<string> userIds, CancellationToken token = default);

        /// <summary>
        /// Record an award and make it the member's verified rank in its field, in one transaction.
        /// </summary>
        /// <param name="award">Award row (action Award).</param>
        /// <param name="rank">Rank row (replaces the member's row for the field).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task AwardAsync(RankPromotion award, MemberRank rank, CancellationToken token = default);

        /// <summary>
        /// Revoke a standing award: stamp it, insert the revoke row, and mark the member's rank Revoked when it came from
        /// this award, in one transaction.
        /// </summary>
        /// <param name="awardId">Award identifier.</param>
        /// <param name="revoke">Revoke row (action Revoke, awardId set).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>False when the award does not exist or was already revoked.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<bool> RevokeAsync(string awardId, RankPromotion revoke, CancellationToken token = default);

        /// <summary>
        /// Read one award or revoke.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Row, or null.</returns>
        Task<RankPromotion?> ReadPromotionAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate awards and revokes.
        /// </summary>
        /// <param name="request">Normalized request (RankCollections.Promotions).</param>
        /// <param name="filter">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<RankPromotion>> EnumeratePromotionsAsync(EnumerationRequest request, RankPromotionFilter filter, CancellationToken token = default);

        /// <summary>
        /// A member's most recent awards and revokes, newest first.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="limit">Maximum rows (1 to 100).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task<List<RankPromotion>> RecentPromotionsForUserAsync(string userId, int limit, CancellationToken token = default);

        /// <summary>
        /// A member's awards and revokes in identifier order after an identifier (account export).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="afterId">Identifier position, or null.</param>
        /// <param name="limit">Maximum rows (1 to 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task<List<RankPromotion>> ListPromotionsForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Count a member's awards and revokes.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task<long> CountPromotionsForUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Every requirement of one Rank field (at most 200).
        /// </summary>
        /// <param name="fieldKey">Rank field key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Requirements in position order.</returns>
        /// <exception cref="ArgumentNullException">The key is null or empty.</exception>
        Task<List<RankRequirement>> ListRequirementsAsync(string fieldKey, CancellationToken token = default);

        /// <summary>
        /// Enumerate requirements.
        /// </summary>
        /// <param name="request">Normalized request (RankCollections.Requirements).</param>
        /// <param name="fieldKey">Rank field key, or null for every field.</param>
        /// <param name="optionKey">Option key, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        Task<EnumerationResult<RankRequirement>> EnumerateRequirementsAsync(EnumerationRequest request, string? fieldKey, string? optionKey, CancellationToken token = default);

        /// <summary>
        /// Replace every requirement of one Rank field (positions follow the list order), with labels and tags, in one
        /// transaction.
        /// </summary>
        /// <param name="fieldKey">Rank field key.</param>
        /// <param name="requirements">New requirements.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task ReplaceRequirementsAsync(string fieldKey, List<RankRequirement> requirements, CancellationToken token = default);

        /// <summary>
        /// A member's lineage entries, optionally of one field, in display order (at most 200).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="fieldKey">Rank field key, or null for every field.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task<List<LineageEntry>> ListLineageAsync(string userId, string? fieldKey, CancellationToken token = default);

        /// <summary>
        /// Read one lineage entry.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entry, or null.</returns>
        Task<LineageEntry?> ReadLineageAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Replace a member's lineage of one field, in one transaction (entries keep their identifiers when set).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="fieldKey">Rank field key.</param>
        /// <param name="entries">Entries in display order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task ReplaceLineageAsync(string userId, string fieldKey, List<LineageEntry> entries, CancellationToken token = default);

        /// <summary>
        /// Mark a lineage entry verified.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="verifierId">Verifier, or null for the admin token.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the entry exists.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task<bool> VerifyLineageAsync(string id, string? verifierId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate the members (not deleted or banned) who hold a value in a Rank field or a rank row for it.
        /// </summary>
        /// <param name="request">Normalized request (RankCollections.Eligibility).</param>
        /// <param name="fieldId">Rank profile field identifier.</param>
        /// <param name="fieldKey">Rank profile field key.</param>
        /// <param name="optionKey">Only members whose verified or self-reported option is this key, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of member identifiers.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<string>> EnumerateCandidatesAsync(EnumerationRequest request, string fieldId, string fieldKey, string? optionKey, CancellationToken token = default);

        /// <summary>
        /// Delete a member's rank rows, awards and revokes about them, and lineage, with their labels and tags (account
        /// deletion). Rows the member wrote about other members keep their actor identifier.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task EraseForUserAsync(string userId, CancellationToken token = default);
    }
}
