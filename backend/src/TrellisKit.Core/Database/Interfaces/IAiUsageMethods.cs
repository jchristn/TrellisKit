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
    /// Usage rollups, cost, budgets, and analytics aggregates (aiusagerollups, aiusageapplied, aibudgetalerts, and
    /// counts over the conversation tables). Implementations are bound to one property and safe to call concurrently.
    /// </summary>
    public interface IAiUsageMethods
    {
        /// <summary>
        /// Add one model call to its hourly rollup row. With a guard key, the call is applied at most once per
        /// (guard, purpose), so a retried write cannot double count.
        /// </summary>
        /// <param name="record">Call.</param>
        /// <param name="guardKey">Idempotency key (a message id), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when applied.</returns>
        Task<bool> ApplyAsync(AiUsageRecord record, string? guardKey, CancellationToken token = default);

        /// <summary>
        /// Totals over a window (non-test traffic unless includeTest), optionally for one assistant or endpoint.
        /// </summary>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="assistantId">Assistant, or null.</param>
        /// <param name="endpointId">Endpoint, or null.</param>
        /// <param name="includeTest">Whether test traffic counts.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Aggregate row.</returns>
        Task<AiUsageRollup> TotalsAsync(DateTime sinceUtc, string? assistantId, string? endpointId, bool includeTest, CancellationToken token = default);

        /// <summary>
        /// Totals per period (hour or day) over a window, oldest first (at most 400 periods).
        /// </summary>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="granularity">Hour or Day.</param>
        /// <param name="assistantId">Assistant, or null.</param>
        /// <param name="endpointId">Endpoint, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One aggregate per period with data.</returns>
        Task<List<AiUsageRollup>> SeriesAsync(DateTime sinceUtc, AiUsageGranularityEnum granularity, string? assistantId, string? endpointId, CancellationToken token = default);

        /// <summary>
        /// Requests per purpose of a set of endpoints over a window.
        /// </summary>
        /// <param name="endpointIds">Endpoint ids (at most 500).</param>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows with EndpointId, Purpose, and Requests set.</returns>
        Task<List<AiUsageRollup>> PurposesAsync(IEnumerable<string> endpointIds, DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate endpoints (live and deleted) with their totals over a window (AiCollections.EndpointAnalytics).
        /// Each row's EndpointId is the endpoint id and Purpose carries "name|apiType|model|deleted".
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="assistantId">Assistant, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<AiUsageRollup>> EnumerateEndpointTotalsAsync(EnumerationRequest request, DateTime sinceUtc, string? assistantId, CancellationToken token = default);

        /// <summary>
        /// Enumerate assistants (live and deleted) with their totals over a window (AiCollections.AssistantAnalytics).
        /// Each row's AssistantId is the assistant id and Purpose carries "name|deleted".
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<AiUsageRollup>> EnumerateAssistantTotalsAsync(EnumerationRequest request, DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate members and visitors with their messages, tokens, and cost over a window
        /// (AiCollections.MemberAnalytics). Date fields hold the last active day.
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="sinceDate">First day, yyyy-MM-dd.</param>
        /// <param name="filter">Filters (assistantId, subjectType).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<AiDailyUsage>> EnumerateMemberTotalsAsync(EnumerationRequest request, string sinceDate, AiEnumerationFilter filter, CancellationToken token = default);

        /// <summary>
        /// Conversation, message, and feedback counts over a window.
        /// </summary>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="assistantId">Assistant, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts.</returns>
        Task<AiActivityCounts> ActivityAsync(DateTime sinceUtc, string? assistantId, CancellationToken token = default);

        /// <summary>
        /// Counts per assistant over a window (conversations, messages, thumbs up, thumbs down).
        /// </summary>
        /// <param name="assistantIds">Assistant ids (at most 500).</param>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assistant id to counts.</returns>
        Task<Dictionary<string, AiActivityCounts>> ActivityByAssistantAsync(IEnumerable<string> assistantIds, DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// Non-test cost since a time, optionally for one assistant, in millionths of a US dollar.
        /// </summary>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="assistantId">Assistant, or null for the whole property.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Cost.</returns>
        Task<long> CostSinceAsync(DateTime sinceUtc, string? assistantId, CancellationToken token = default);

        /// <summary>
        /// Record that a budget alert level was sent for a month; false when it was already recorded.
        /// </summary>
        /// <param name="scope">Property or Assistant.</param>
        /// <param name="scopeId">Property or assistant id.</param>
        /// <param name="month">UTC month, yyyy-MM.</param>
        /// <param name="level">Percent level, for example 80 or 100.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when newly recorded.</returns>
        Task<bool> TryRecordBudgetAlertAsync(string scope, string scopeId, string month, int level, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Fold hourly rows older than a time into daily rows, and delete daily rows and guard rows older than the
        /// retention cutoff.
        /// </summary>
        /// <param name="compactBeforeUtc">Hourly rows before this time are compacted.</param>
        /// <param name="retainAfterUtc">Rows before this time are deleted.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows changed.</returns>
        Task<int> CompactAsync(DateTime compactBeforeUtc, DateTime retainAfterUtc, CancellationToken token = default);
    }
}
