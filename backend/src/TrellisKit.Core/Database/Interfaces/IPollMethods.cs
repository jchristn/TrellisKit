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
    /// Poll, option, and vote methods (docs/PHASE3_CONTRACT.md section 6).
    /// </summary>
    public interface IPollMethods
    {
        /// <summary>
        /// Read a poll with its options, or null.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<Poll?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read the poll of a thread, or null.
        /// </summary>
        /// <param name="threadId">Thread id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<Poll?> ReadByThreadAsync(string threadId, CancellationToken token = default);

        /// <summary>
        /// The polls of several threads, keyed by thread id.
        /// </summary>
        /// <param name="threadIds">Thread ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<Dictionary<string, Poll>> ReadByThreadsAsync(IEnumerable<string> threadIds, CancellationToken token = default);

        /// <summary>
        /// A member's chosen options per poll.
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="pollIds">Poll ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<Dictionary<string, List<string>>> VotesOfAsync(string userId, IEnumerable<string> pollIds, CancellationToken token = default);

        /// <summary>
        /// Replace a member's vote (empty removes it) in one batch serialized per poll; closed polls and locked votes are left unchanged and reported.
        /// </summary>
        /// <param name="pollId">Poll id.</param>
        /// <param name="userId">User id.</param>
        /// <param name="optionIds">Option ids.</param>
        /// <param name="allowChange">Allow change.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<PollVoteOutcome> VoteAsync(string pollId, string userId, IReadOnlyCollection<string> optionIds, bool allowChange, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Close an open poll.
        /// </summary>
        /// <param name="pollId">Poll id.</param>
        /// <param name="byUserId">By user id.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> CloseAsync(string pollId, string? byUserId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Close polls whose scheduled close passed (conditional claim) and return them.
        /// </summary>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<Poll>> ClaimDueAsync(DateTime nowUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Replace a poll's options and settings before the first vote; false after a vote or close.
        /// </summary>
        /// <param name="poll">Poll.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> ReplaceOptionsAsync(Poll poll, CancellationToken token = default);

        /// <summary>
        /// Change an open poll's scheduled close.
        /// </summary>
        /// <param name="pollId">Poll id.</param>
        /// <param name="closesUtc">Closes utc.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> SetClosesAsync(string pollId, DateTime? closesUtc, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate a poll's voters with their options, optionally only voters of one option.
        /// </summary>
        /// <param name="pollId">Poll id.</param>
        /// <param name="optionId">Option id.</param>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<PollVoter>> EnumerateVotersAsync(string pollId, string? optionId, EnumerationRequest request, CancellationToken token = default);

        /// <summary>
        /// Voters of a poll in user id order after a cursor.
        /// </summary>
        /// <param name="pollId">Poll id.</param>
        /// <param name="afterUserId">After user id.</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<string>> VoterIdsAsync(string pollId, string? afterUserId, int limit, CancellationToken token = default);

        /// <summary>
        /// Whether the member voted in the poll since a time.
        /// </summary>
        /// <param name="pollId">Poll id.</param>
        /// <param name="userId">User id.</param>
        /// <param name="sinceUtc">Since utc.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> VotedWithinAsync(string pollId, string userId, DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// A member's votes (export).
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="afterId">After id.</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<PollVoteRecord>> ListVotesByUserAsync(string userId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Delete a member's votes and recompute the affected polls.
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<int> DeleteVotesForUserAsync(string userId, DateTime nowUtc, CancellationToken token = default);
    }
}
