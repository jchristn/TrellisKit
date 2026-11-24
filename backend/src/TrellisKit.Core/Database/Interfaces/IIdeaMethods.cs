namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Ideas board methods (docs/PHASE5_CONTRACT.md sections 7 and 16): statuses, ideas, votes, follows, merges, the
    /// trending recompute, account lifecycle, and the two ticket statements of publish-idea and link-idea. Every
    /// statement is bound to the driver's property.
    /// </summary>
    public interface IIdeaMethods
    {
        /// <summary>
        /// Every status in display order (bounded by the status count, at most 200).
        /// </summary>
        /// <param name="includeRetired">Include retired statuses.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Statuses.</returns>
        Task<List<IdeaStatus>> ListStatusesAsync(bool includeRetired, CancellationToken token = default);

        /// <summary>
        /// Enumerate statuses.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="includeRetired">Include retired statuses.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<IdeaStatus>> EnumerateStatusesAsync(EnumerationRequest request, bool includeRetired, CancellationToken token = default);

        /// <summary>
        /// Read a status, or null.
        /// </summary>
        /// <param name="id">Status identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status, or null.</returns>
        Task<IdeaStatus?> ReadStatusAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Count statuses (retired included).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountStatusesAsync(CancellationToken token = default);

        /// <summary>
        /// Insert a status; an initial status clears the flag on every other status in the same transaction.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored status.</returns>
        /// <exception cref="Exceptions.DuplicateRecordException">The key is in use.</exception>
        Task<IdeaStatus> CreateStatusAsync(IdeaStatus status, CancellationToken token = default);

        /// <summary>
        /// Update a status (key, label, color, closed, initial, retired); renaming the key renames it on every idea.
        /// </summary>
        /// <param name="status">Status with the new values.</param>
        /// <param name="previousKey">The key before the update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored status, or null when missing.</returns>
        /// <exception cref="Exceptions.DuplicateRecordException">The key is in use.</exception>
        Task<IdeaStatus?> UpdateStatusAsync(IdeaStatus status, string previousKey, CancellationToken token = default);

        /// <summary>
        /// Set the display order (sortorder 10, 20, ... in the given order).
        /// </summary>
        /// <param name="ids">Status identifiers.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows updated.</returns>
        Task<int> ReorderStatusesAsync(List<string> ids, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Count live ideas with a status key.
        /// </summary>
        /// <param name="key">Status key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountWithStatusAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Insert an idea, optionally with a vote and a follow by one member, and recompute its vote count.
        /// </summary>
        /// <param name="idea">Idea.</param>
        /// <param name="voterUserId">Member who votes and follows, or null.</param>
        /// <param name="followSource">Source of that member's follow.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored idea.</returns>
        Task<Idea> CreateAsync(Idea idea, string? voterUserId, IdeaFollowSourceEnum followSource, CancellationToken token = default);

        /// <summary>
        /// Read an idea (any state), or null.
        /// </summary>
        /// <param name="id">Idea identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Idea, or null.</returns>
        Task<Idea?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read several ideas (any state).
        /// </summary>
        /// <param name="ids">Identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Ideas found.</returns>
        Task<List<Idea>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Store title, body, audience, showAuthor, editedUtc, and updatedUtc.
        /// </summary>
        /// <param name="idea">Idea.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored idea, or null when missing.</returns>
        Task<Idea?> UpdateAsync(Idea idea, CancellationToken token = default);

        /// <summary>
        /// Set an idea's status and note.
        /// </summary>
        /// <param name="id">Idea identifier.</param>
        /// <param name="statusKey">Status key.</param>
        /// <param name="statusNote">Note, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored idea, or null when missing.</returns>
        Task<Idea?> SetStatusAsync(string id, string statusKey, string? statusNote, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate ideas.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="filter">Conditions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<Idea>> EnumerateAsync(EnumerationRequest request, IdeaFilter filter, CancellationToken token = default);

        /// <summary>
        /// Add a member's vote and follow (idempotent) and recompute the vote count.
        /// </summary>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the vote was added.</returns>
        Task<bool> VoteAsync(string ideaId, string userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Remove a member's vote (idempotent) and recompute the vote count.
        /// </summary>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a vote was removed.</returns>
        Task<bool> UnvoteAsync(string ideaId, string userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// The ideas among the given ones a member voted for.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="ideaIds">Idea identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers.</returns>
        Task<HashSet<string>> VotedAmongAsync(string userId, IEnumerable<string> ideaIds, CancellationToken token = default);

        /// <summary>
        /// Follow an idea (idempotent).
        /// </summary>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="userId">Member.</param>
        /// <param name="source">Source.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when added.</returns>
        Task<bool> FollowAsync(string ideaId, string userId, IdeaFollowSourceEnum source, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Unfollow an idea (idempotent).
        /// </summary>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when removed.</returns>
        Task<bool> UnfollowAsync(string ideaId, string userId, CancellationToken token = default);

        /// <summary>
        /// The ideas among the given ones a member follows.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="ideaIds">Idea identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers.</returns>
        Task<HashSet<string>> FollowedAmongAsync(string userId, IEnumerable<string> ideaIds, CancellationToken token = default);

        /// <summary>
        /// One keyset batch of an idea's voters and followers (distinct member ids in ascending order).
        /// </summary>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="afterUserId">Last member of the previous batch, or null.</param>
        /// <param name="limit">Batch size.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member identifiers.</returns>
        Task<List<string>> RecipientIdsAsync(string ideaId, string? afterUserId, int limit, CancellationToken token = default);

        /// <summary>
        /// One keyset batch of an idea's voters (ascending member ids).
        /// </summary>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="afterUserId">Last member of the previous batch, or null.</param>
        /// <param name="limit">Batch size.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member identifiers.</returns>
        Task<List<string>> VoterIdsAsync(string ideaId, string? afterUserId, int limit, CancellationToken token = default);

        /// <summary>
        /// The members among the given ones who voted for an idea.
        /// </summary>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="userIds">Members.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member identifiers.</returns>
        Task<HashSet<string>> VotersAmongAsync(string ideaId, IEnumerable<string> userIds, CancellationToken token = default);

        /// <summary>
        /// An idea's votes split by the voters' membership state.
        /// </summary>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="nowUtc">Now (entitlement activity).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts.</returns>
        Task<IdeaVoterCounts> VoterCountsAsync(string ideaId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Merge an idea into another: set mergedintoid, copy its votes to the target (de-duplicated by member), make its
        /// followers follow the target (source Merge), and recompute the target's vote count, in one transaction. The
        /// merged idea keeps its own votes and comments.
        /// </summary>
        /// <param name="sourceId">Merged idea.</param>
        /// <param name="targetId">Target idea.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when merged.</returns>
        Task<bool> MergeAsync(string sourceId, string targetId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Clear mergedintoid (votes copied to the target stay there).
        /// </summary>
        /// <param name="id">Idea identifier.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when unmerged.</returns>
        Task<bool> UnmergeAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Soft delete an idea (and its labels and tags).
        /// </summary>
        /// <param name="id">Idea identifier.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> SoftDeleteAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Purge a removed or deleted idea after the retention period (content purge worker): title, body, and status
        /// note cleared, labels, tags, and mentions deleted, purgedutc set. Votes and comments are left to their own rules.
        /// </summary>
        /// <param name="id">Idea identifier.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call purged the idea.</returns>
        Task<bool> PurgeAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Set the moderation state.
        /// </summary>
        /// <param name="id">Idea identifier.</param>
        /// <param name="state">State.</param>
        /// <param name="moderatorUserId">Moderator, or null (system).</param>
        /// <param name="reason">Reason, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when updated.</returns>
        Task<bool> SetModerationStateAsync(string id, ModerationStateEnum state, string? moderatorUserId, string? reason, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Recompute the open report count from the reports table.
        /// </summary>
        /// <param name="id">Idea identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows updated.</returns>
        Task<int> RecomputeOpenReportCountAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Recompute recentvotecount of every idea from votes cast at or after the window start.
        /// </summary>
        /// <param name="windowStartUtc">Window start.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Ideas whose count changed.</returns>
        Task<int> RecomputeTrendingAsync(DateTime windowStartUtc, CancellationToken token = default);

        /// <summary>
        /// Account deletion: delete the member's votes (recomputing the affected counts) and follows, soft delete the
        /// ideas they authored that are still in the initial status, and clear the author of the others.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="initialStatusKey">Initial status key.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Ideas deleted.</returns>
        Task<int> EraseForUserAsync(string userId, string initialStatusKey, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// A member's live ideas by identifier (keyset, export).
        /// </summary>
        /// <param name="authorUserId">Author.</param>
        /// <param name="afterId">Last identifier of the previous page, or null.</param>
        /// <param name="limit">Page size.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Ideas.</returns>
        Task<List<Idea>> ListByAuthorAsync(string authorUserId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// A member's votes by identifier (keyset, export).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="afterId">Last vote identifier of the previous page, or null.</param>
        /// <param name="limit">Page size.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Votes.</returns>
        Task<List<IdeaVoteRecord>> ListVotesByUserAsync(string userId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Read the ticket columns publish-idea and link-idea use, or null.
        /// </summary>
        /// <param name="ticketId">Ticket identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Ticket, or null.</returns>
        Task<IdeaTicket?> ReadTicketAsync(string ticketId, CancellationToken token = default);

        /// <summary>
        /// Set a ticket's linked idea.
        /// </summary>
        /// <param name="ticketId">Ticket identifier.</param>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when updated.</returns>
        Task<bool> LinkTicketAsync(string ticketId, string ideaId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Refresh the unread IdeaStatusChanged notification of an idea for the given recipients (aggregation per idea
        /// while unread): new title, body, and time, push queued again.
        /// </summary>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="recipientUserIds">Recipients.</param>
        /// <param name="title">Title.</param>
        /// <param name="body">Body, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The recipients whose notification was refreshed.</returns>
        Task<HashSet<string>> RefreshStatusNotificationsAsync(string ideaId, IEnumerable<string> recipientUserIds, string title, string? body, DateTime nowUtc, CancellationToken token = default);
    }
}
