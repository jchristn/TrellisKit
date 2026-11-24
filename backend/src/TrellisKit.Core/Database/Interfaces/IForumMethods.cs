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
    /// Forum, forum follow, forum read state, forum mute, and forum moderator methods (docs/PHASE3_CONTRACT.md sections 3, 9, 11, and 17).
    /// </summary>
    public interface IForumMethods
    {
        /// <summary>
        /// Create a forum with its allowed thread types.
        /// </summary>
        /// <param name="forum">Forum.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<Forum> CreateAsync(Forum forum, CancellationToken token = default);

        /// <summary>
        /// Read an undeleted forum, or null.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<Forum?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read an undeleted forum by slug (case-insensitive), or null.
        /// </summary>
        /// <param name="slug">Slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<Forum?> ReadBySlugAsync(string slug, CancellationToken token = default);

        /// <summary>
        /// Read several forums (deleted included).
        /// </summary>
        /// <param name="ids">Ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<Forum>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Store a forum's fields and allowed types; optionally narrow every thread audience broader than the forum's in the same batch.
        /// </summary>
        /// <param name="forum">Forum.</param>
        /// <param name="narrowThreads">Narrow threads.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<Forum> UpdateAsync(Forum forum, bool narrowThreads, CancellationToken token = default);

        /// <summary>
        /// Undeleted forums.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<long> CountAsync(CancellationToken token = default);

        /// <summary>
        /// The sort order that places a new forum last.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<int> NextSortOrderAsync(CancellationToken token = default);

        /// <summary>
        /// Identifiers of undeleted, non-archived forums in sort order.
        /// </summary>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<string>> ActiveIdsAsync(int limit, CancellationToken token = default);

        /// <summary>
        /// Assign sort orders 10, 20, 30 in list order; returns the forums found.
        /// </summary>
        /// <param name="forumIds">Forum ids.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<int> ReorderAsync(List<string> forumIds, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Archive or unarchive a forum.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="archived">Archived.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> SetArchivedAsync(string id, bool archived, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Soft-delete a forum that has no threads (in any state not yet purged).
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> DeleteAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Whether a forum has threads that are not deleted (deleted threads, purged or not, never block a forum delete).
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> HasThreadsAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate forums with the filter.
        /// </summary>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<Forum>> EnumerateAsync(EnumerationRequest request, ForumFilter filter, CancellationToken token = default);

        /// <summary>
        /// Follow a forum (idempotent).
        /// </summary>
        /// <param name="follow">Follow.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> FollowAsync(ForumFollow follow, CancellationToken token = default);

        /// <summary>
        /// Unfollow a forum.
        /// </summary>
        /// <param name="forumId">Forum id.</param>
        /// <param name="userId">User id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> UnfollowAsync(string forumId, string userId, CancellationToken token = default);

        /// <summary>
        /// Forums among the identifiers that the member follows.
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="forumIds">Forum ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<HashSet<string>> FollowedAmongAsync(string userId, IEnumerable<string> forumIds, CancellationToken token = default);

        /// <summary>
        /// Followers of a forum in user id order after a cursor.
        /// </summary>
        /// <param name="forumId">Forum id.</param>
        /// <param name="afterUserId">After user id.</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<string>> FollowerIdsAsync(string forumId, string? afterUserId, int limit, CancellationToken token = default);

        /// <summary>
        /// Record that a member read a forum (every thread in it).
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="forumId">Forum id.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        Task MarkReadAsync(string userId, string forumId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// A member's last read times of several forums.
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="forumIds">Forum ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<Dictionary<string, DateTime>> ReadTimesAsync(string userId, IEnumerable<string> forumIds, CancellationToken token = default);

        /// <summary>
        /// Unread live threads per forum for a member, capped.
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="forumIds">Forum ids.</param>
        /// <param name="audiences">Audiences.</param>
        /// <param name="hiddenAuthorIds">Hidden author ids.</param>
        /// <param name="windowStartUtc">Window start utc.</param>
        /// <param name="cap">Cap.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<Dictionary<string, int>> UnreadCountsAsync(string userId, IEnumerable<string> forumIds, List<ContentAudienceEnum> audiences, List<string> hiddenAuthorIds, DateTime windowStartUtc, int cap, CancellationToken token = default);

        /// <summary>
        /// Create a forum mute, ending any active mute of the member in the forum.
        /// </summary>
        /// <param name="mute">Mute.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<ForumMute> CreateMuteAsync(ForumMute mute, CancellationToken token = default);

        /// <summary>
        /// Read a forum mute, or null.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<ForumMute?> ReadMuteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// The member's active mute in a forum, or null.
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="forumId">Forum id.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<ForumMute?> ActiveMuteAsync(string userId, string forumId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Lift an active mute.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="byUserId">By user id.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool> EndMuteAsync(string id, string? byUserId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate forum mutes.
        /// </summary>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<ForumMute>> EnumerateMutesAsync(EnumerationRequest request, ForumMuteFilter filter, CancellationToken token = default);

        /// <summary>
        /// The newest forum mutes received by a member (export).
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="limit">Maximum rows. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<ForumMute>> ListMutesForUserAsync(string userId, int limit, CancellationToken token = default);

        /// <summary>
        /// Delete the forum mutes of a member (account deletion).
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<int> DeleteMutesForUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// A member's auto-follow on comment preference, or null when unset.
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<bool?> ReadAutoFollowOnCommentAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Store a member's auto-follow on comment preference.
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <param name="value">Value.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        Task WriteAutoFollowOnCommentAsync(string userId, bool value, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate the members whose Moderator assignments cover a forum.
        /// </summary>
        /// <param name="forumId">Forum id.</param>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<EnumerationResult<string>> EnumerateModeratorsAsync(string forumId, EnumerationRequest request, CancellationToken token = default);

        /// <summary>
        /// Members whose Moderator assignments cover a forum.
        /// </summary>
        /// <param name="forumId">Forum id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<long> CountModeratorsAsync(string forumId, CancellationToken token = default);

        /// <summary>
        /// Active members whose assignments cover a forum, plus SystemAdmins.
        /// </summary>
        /// <param name="forumId">Forum id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<List<string>> ModeratorIdsForForumAsync(string forumId, CancellationToken token = default);

        /// <summary>
        /// Open Forum-scope reports per forum.
        /// </summary>
        /// <param name="forumIds">Forum ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        Task<Dictionary<string, long>> OpenReportCountsAsync(IEnumerable<string> forumIds, CancellationToken token = default);
    }
}
