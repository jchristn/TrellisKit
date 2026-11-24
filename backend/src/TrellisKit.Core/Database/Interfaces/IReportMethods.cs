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
    /// Report data access (contract sections 9.1, 10.2, and 15). Implementations are safe to call concurrently: one
    /// Open report per reporter and target is enforced by ux_reports_open, auto-hide is a conditional update of the
    /// content row, and overdue notices are claimed with a conditional update.
    /// </summary>
    public interface IReportMethods
    {
        /// <summary>
        /// Insert a member report unless the reporter already has an Open report on the target.
        /// </summary>
        /// <param name="report">Report (id, reporter, target, reason, due, and created set).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored report, or null when an Open report of the same reporter already exists.</returns>
        /// <exception cref="ArgumentNullException">The report or a required field is null or empty.</exception>
        Task<Report?> CreateAsync(Report report, CancellationToken token = default);

        /// <summary>
        /// Read a report.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Report, or null.</returns>
        Task<Report?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// The reporter's Open report on a target.
        /// </summary>
        /// <param name="reporterUserId">Reporter.</param>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Report, or null.</returns>
        Task<Report?> ReadOpenByReporterAsync(string reporterUserId, TargetTypeEnum targetType, string targetId, CancellationToken token = default);

        /// <summary>
        /// Auto-hide (contract section 9.1) in one transaction: set the FeedPost or Comment Hidden (no moderator) when it
        /// is not deleted, its moderation state is None, and the distinct member reporters with Open reports reach the
        /// threshold, then insert the AutoHide action only when this call hid it. Concurrent callers hide exactly once.
        /// </summary>
        /// <param name="action">The AutoHide action to insert (id, target, author, reason, created).</param>
        /// <param name="threshold">moderation.autoHideReportThreshold (positive).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call hid the content.</returns>
        /// <exception cref="ArgumentNullException">The action is null.</exception>
        /// <exception cref="ArgumentException">The target type is not FeedPost or Comment.</exception>
        Task<bool> TryAutoHideAsync(ModerationAction action, int threshold, CancellationToken token = default);

        /// <summary>
        /// Resolve every Open report on a target (in the given scopes) with a status, resolver, note, and action.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="status">ActionTaken or Dismissed.</param>
        /// <param name="resolvedByUserId">Resolver, or null (admin token).</param>
        /// <param name="note">Resolution note, or null.</param>
        /// <param name="moderationActionId">Resolving action, or null.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="scopeTypes">Visible scope types, or null for every scope.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Reports resolved.</returns>
        Task<int> ResolveOpenAsync(TargetTypeEnum targetType, string targetId, ReportStatusEnum status, string? resolvedByUserId, string? note, string? moderationActionId,
            DateTime nowUtc, List<string>? scopeTypes, CancellationToken token = default);

        /// <summary>
        /// One page of the moderation queue (ModerationCollections.Queue): one aggregated row per target.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="filter">Typed conditions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with exact totals.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<ModerationQueueRow>> EnumerateQueueAsync(EnumerationRequest request, ModerationQueueFilter filter, CancellationToken token = default);

        /// <summary>
        /// Report counts per reason for a bounded page of queue rows (Open reports for Open rows, every report for
        /// Resolved rows).
        /// </summary>
        /// <param name="rows">Queue rows (a page).</param>
        /// <param name="scopeTypes">Visible scope types, or null for every scope.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts keyed by "targetType:targetId", then reason.</returns>
        /// <exception cref="ArgumentNullException">The rows are null.</exception>
        Task<Dictionary<string, Dictionary<string, long>>> ReasonCountsAsync(IReadOnlyList<ModerationQueueRow> rows, List<string>? scopeTypes, CancellationToken token = default);

        /// <summary>
        /// One page of reports (ModerationCollections.Reports).
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="filter">Typed conditions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with exact totals.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<Report>> EnumerateAsync(EnumerationRequest request, ReportFilter filter, CancellationToken token = default);

        /// <summary>
        /// Open reports in the given scopes, optionally only those due before a time.
        /// </summary>
        /// <param name="scopeTypes">Visible scope types, or null for every scope.</param>
        /// <param name="dueBeforeUtc">Only reports with dueutc before this time, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountOpenAsync(List<string>? scopeTypes, DateTime? dueBeforeUtc, CancellationToken token = default);

        /// <summary>
        /// Targets with Open reports in the given scopes (queue rows).
        /// </summary>
        /// <param name="scopeTypes">Visible scope types, or null for every scope.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountOpenTargetsAsync(List<string>? scopeTypes, CancellationToken token = default);

        /// <summary>
        /// Open reports on one target in the given scopes.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="scopeTypes">Visible scope types, or null for every scope.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountOpenOnTargetAsync(TargetTypeEnum targetType, string targetId, List<string>? scopeTypes, CancellationToken token = default);

        /// <summary>
        /// Claim Open reports past their review target that have not been notified yet: sets overduenotifiedutc and
        /// returns the claimed rows (earliest due first).
        /// </summary>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="limit">Most reports.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Claimed reports.</returns>
        Task<List<ModerationOverdueClaim>> ClaimOverdueAsync(DateTime nowUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Reports on a member's content or profile (targetauthoruserid) since a time.
        /// </summary>
        /// <param name="authorUserId">Member.</param>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountAgainstAuthorAsync(string authorUserId, DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// A keyset batch of the reports a member filed, by identifier (account export).
        /// </summary>
        /// <param name="reporterUserId">Reporter.</param>
        /// <param name="afterId">Continue after this identifier, or null.</param>
        /// <param name="limit">Most reports.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Reports in identifier order.</returns>
        /// <exception cref="ArgumentNullException">The reporter is null or empty.</exception>
        Task<List<Report>> ListByReporterAsync(string reporterUserId, string? afterId, int limit, CancellationToken token = default);
    }
}
