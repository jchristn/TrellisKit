namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Word filter data access (wordfilterterms, wordfilterhits, and the system reports the filter files). Implementations
    /// are safe to call concurrently.
    /// </summary>
    public interface IWordFilterMethods
    {
        /// <summary>
        /// Create a term.
        /// </summary>
        /// <param name="term">Term with its normalized form.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored term.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="DuplicateRecordException">A term with the same normalized form exists.</exception>
        Task<WordFilterTerm> CreateAsync(WordFilterTerm term, CancellationToken token = default);

        /// <summary>
        /// Read a term.
        /// </summary>
        /// <param name="id">Term identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Term, or null.</returns>
        Task<WordFilterTerm?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Update a term.
        /// </summary>
        /// <param name="term">Term.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored term.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="DuplicateRecordException">Another term has the same normalized form.</exception>
        Task<WordFilterTerm> UpdateAsync(WordFilterTerm term, CancellationToken token = default);

        /// <summary>
        /// Delete a term and its labels and tags in one batch (its hits are kept until pruned).
        /// </summary>
        /// <param name="id">Term identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row was removed.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Insert many terms, skipping those whose normalized form exists.
        /// </summary>
        /// <param name="terms">Terms (at most 1000 per call).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Terms added.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<int> ImportAsync(List<WordFilterTerm> terms, CancellationToken token = default);

        /// <summary>
        /// Every term, for the in-memory matcher. The list is bounded by the admin-maintained term list (import is
        /// capped at 1000 per call) and is never served to clients.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Terms.</returns>
        Task<List<WordFilterTerm>> EnumerateAllAsync(CancellationToken token = default);

        /// <summary>
        /// One enumeration page of terms for the admin console (collection SafetyCollections.WordFilterTerms) with hit
        /// counts since <paramref name="hitsSinceUtc"/>. Labels and tags are not loaded here.
        /// </summary>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="filter">Typed filters.</param>
        /// <param name="hitsSinceUtc">Start of the hit count window.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<WordFilterTermView>> EnumerateAsync(EnumerationRequest request, WordFilterFilter filter, DateTime hitsSinceUtc, CancellationToken token = default);

        /// <summary>
        /// Record matches.
        /// </summary>
        /// <param name="hits">Hits.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task CreateHitsAsync(List<WordFilterHit> hits, CancellationToken token = default);

        /// <summary>
        /// Hits recorded for a term since a time.
        /// </summary>
        /// <param name="termId">Term identifier.</param>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountHitsAsync(string termId, DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// Delete hits recorded before the cutoff.
        /// </summary>
        /// <param name="cutoffUtc">Cutoff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows removed.</returns>
        Task<int> PruneHitsAsync(DateTime cutoffUtc, CancellationToken token = default);

        /// <summary>
        /// File a system report (no reporter, reason Filter, status Open, scope Feed "*") for saved content unless an
        /// Open system report already exists for it (race-safe through the ux_reports_system_open partial index).
        /// </summary>
        /// <param name="reportId">New report identifier (rpt_ prefix).</param>
        /// <param name="targetType">Content type.</param>
        /// <param name="targetId">Content identifier.</param>
        /// <param name="targetAuthorUserId">Content author, or null.</param>
        /// <param name="details">Details for moderators (truncated to 1000 characters).</param>
        /// <param name="nowUtc">Creation time.</param>
        /// <param name="dueUtc">Review due time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a report was created; false when an Open system report already existed.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> CreateSystemReportAsync(string reportId, TargetTypeEnum targetType, string targetId, string? targetAuthorUserId, string? details, DateTime nowUtc, DateTime dueUtc, CancellationToken token = default);

        /// <summary>
        /// File a system report in a given moderation scope (for example scope Assistant with the assistant id for
        /// AssistantMessage targets, docs/AI_CONTRACT.md section 12.3) unless an Open system report already exists for
        /// the target.
        /// </summary>
        /// <param name="reportId">New report identifier (rpt_ prefix).</param>
        /// <param name="targetType">Content type.</param>
        /// <param name="targetId">Content identifier.</param>
        /// <param name="targetAuthorUserId">Content author, or null.</param>
        /// <param name="details">Details for moderators (truncated to 1000 characters).</param>
        /// <param name="scopeType">Scope type (Feed, Forum, Assistant).</param>
        /// <param name="scopeId">Scope identifier, or "*".</param>
        /// <param name="nowUtc">Creation time.</param>
        /// <param name="dueUtc">Review due time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a report was created; false when an Open system report already existed.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> CreateSystemReportAsync(string reportId, TargetTypeEnum targetType, string targetId, string? targetAuthorUserId, string? details, string scopeType, string scopeId, DateTime nowUtc, DateTime dueUtc, CancellationToken token = default);
    }
}
