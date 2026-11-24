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
    /// Moderation action data access (contract sections 9.3, 10.2, 10.4, and 15), plus the moderation reads over
    /// content and moderators: a member's content history, the moderators list with their 30-day statistics, and the
    /// purge candidates of the content purge worker. Implementations are safe to call concurrently.
    /// </summary>
    public interface IModerationActionMethods
    {
        /// <summary>
        /// Insert an action.
        /// </summary>
        /// <param name="action">Action (id, target, action, reason, created set).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored action.</returns>
        /// <exception cref="ArgumentNullException">The action or a required field is null or empty.</exception>
        Task<ModerationAction> CreateAsync(ModerationAction action, CancellationToken token = default);

        /// <summary>
        /// Read an action.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Action, or null.</returns>
        Task<ModerationAction?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// One page of the actions log (ModerationCollections.Actions).
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="filter">Typed conditions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with exact totals.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<ModerationAction>> EnumerateAsync(EnumerationRequest request, ModerationActionFilter filter, CancellationToken token = default);

        /// <summary>
        /// Actions of the given kinds on a member's content since a time.
        /// </summary>
        /// <param name="authorUserId">Author of the targets.</param>
        /// <param name="actions">Action kinds.</param>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountByAuthorAsync(string authorUserId, IEnumerable<ModerationActionTypeEnum> actions, DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// One page of a member's feed posts and comments in any state (ModerationCollections.Content).
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="filter">Typed conditions (authorUserId required).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of target references with exact totals.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or the author is empty.</exception>
        Task<EnumerationResult<ModerationContentRow>> EnumerateContentAsync(EnumerationRequest request, ModerationContentFilter filter, CancellationToken token = default);

        /// <summary>
        /// One page of moderators (ModerationCollections.Moderators): members holding Moderator or FeedModerator who
        /// are not deleted.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="filter">Typed conditions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of member identifiers with exact totals.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<string>> EnumerateModeratorsAsync(EnumerationRequest request, ModeratorFilter filter, CancellationToken token = default);

        /// <summary>
        /// Activity of a bounded page of moderators since a time: actions taken, reports resolved after their review
        /// target, and the median response time over each moderator's most recent resolutions.
        /// </summary>
        /// <param name="userIds">Moderators (a page).</param>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="maxSamples">Most recent resolutions per moderator used for the median (1 to 10000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Statistics keyed by member (every requested member present).</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        Task<Dictionary<string, ModeratorStats>> ModeratorStatsAsync(IEnumerable<string> userIds, DateTime sinceUtc, int maxSamples, CancellationToken token = default);

        /// <summary>
        /// Feed posts and comments due for purging: not purged, removed (moderatedutc) or deleted (deletedutc) before the
        /// cutoff, and no Open report references them. Oldest identifiers first, at most limit of each type.
        /// </summary>
        /// <param name="cutoffUtc">Now minus moderation.retainRemovedDays.</param>
        /// <param name="limit">Most candidates per type.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Candidates.</returns>
        Task<List<PurgeCandidate>> ListPurgeCandidatesAsync(DateTime cutoffUtc, int limit, CancellationToken token = default);
    }
}
