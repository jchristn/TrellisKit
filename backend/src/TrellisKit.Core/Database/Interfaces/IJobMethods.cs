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
    /// Job engine storage of one property (docs/INTEGRATION.md). Claims use FOR UPDATE SKIP LOCKED so any
    /// number of server instances can share the work. Thread safety: safe for concurrent use.
    /// </summary>
    public interface IJobMethods
    {
        /// <summary>Enumerate definitions without truncating the collection.</summary>
        /// <param name="request">Normalized query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Definition page.</returns>
        Task<EnumerationResult<JobDefinition>> EnumerateDefinitionsAsync(EnumerationRequest request, CancellationToken token = default);

        /// <summary>Count all definitions, or only enabled definitions.</summary>
        /// <param name="enabledOnly">Only enabled definitions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Exact count.</returns>
        Task<int> CountDefinitionsAsync(bool enabledOnly, CancellationToken token = default);

        /// <summary>
        /// One definition by key, or null.
        /// </summary>
        /// <param name="key">Handler key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Definition or null.</returns>
        Task<JobDefinition?> ReadDefinitionAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Insert a definition unless one exists for the key (seeding from handler defaults).
        /// </summary>
        /// <param name="definition">Definition.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when inserted.</returns>
        /// <exception cref="ArgumentNullException">definition is null.</exception>
        Task<bool> InsertDefinitionIfMissingAsync(JobDefinition definition, CancellationToken token = default);

        /// <summary>
        /// Save every field of a definition.
        /// </summary>
        /// <param name="definition">Definition.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">definition is null.</exception>
        Task UpdateDefinitionAsync(JobDefinition definition, CancellationToken token = default);

        /// <summary>
        /// Lock up to max enabled definitions that are due and not locked.
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="lockUntilUtc">Lock expiry.</param>
        /// <param name="max">Maximum definitions (1 to 100).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Locked definitions.</returns>
        Task<List<JobDefinition>> ClaimDueDefinitionsAsync(DateTime nowUtc, DateTime lockUntilUtc, int max, CancellationToken token = default);

        /// <summary>
        /// Whether the key has a queued or running run.
        /// </summary>
        /// <param name="key">Handler key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when active.</returns>
        Task<bool> HasActiveRunAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Insert a run.
        /// </summary>
        /// <param name="run">Run.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">run is null.</exception>
        Task CreateRunAsync(JobRun run, CancellationToken token = default);

        /// <summary>
        /// One run, or null.
        /// </summary>
        /// <param name="id">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Run or null.</returns>
        Task<JobRun?> ReadRunAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Save the status, error, times, counts, and cost of a run.
        /// </summary>
        /// <param name="run">Run.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">run is null.</exception>
        Task UpdateRunAsync(JobRun run, CancellationToken token = default);

        /// <summary>Persist counts or a terminal result only while the run is still Running.</summary>
        /// <param name="run">Updated run.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>False when another worker or cancellation already finished the run.</returns>
        Task<bool> UpdateRunningRunAsync(JobRun run, CancellationToken token = default);

        /// <summary>Lock the run, skip pending items, and cancel atomically with planning and item claims.</summary>
        /// <param name="id">Run identifier.</param>
        /// <param name="nowUtc">Cancellation time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>False when the run is already terminal.</returns>
        Task<bool> CancelRunAsync(string id, DateTime nowUtc, CancellationToken token = default);


        /// <summary>
        /// Lease up to max queued runs for planning for five minutes, oldest first; expired claims are reclaimed.
        /// </summary>
        /// <param name="workerId">Worker identifier.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="max">Maximum runs (1 to 100).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Claimed runs.</returns>
        Task<List<JobRun>> ClaimQueuedRunsAsync(string workerId, DateTime nowUtc, int max, CancellationToken token = default);

        /// <summary>
        /// Running runs, oldest first.
        /// </summary>
        /// <param name="max">Maximum runs (1 to 500).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Runs.</returns>
        Task<List<JobRun>> ReadRunningRunsAsync(int max, CancellationToken token = default);

        /// <summary>Atomically publish a fenced plan and its items, or a planning failure. False for a stale/cancelled claim.</summary>
        /// <param name="run">Claimed run with the desired Running or Failed status.</param>
        /// <param name="items">Bounded plan items.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Whether this claim still owned the run.</returns>
        Task<bool> FinishPlanningAsync(JobRun run, List<JobItem> items, CancellationToken token = default);

        /// <summary>
        /// Insert items, ignoring duplicate target keys within the run.
        /// </summary>
        /// <param name="items">Items (at most 5000 per call).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Items inserted.</returns>
        /// <exception cref="ArgumentNullException">items is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">More than 5000 items.</exception>
        Task<int> InsertItemsAsync(List<JobItem> items, CancellationToken token = default);

        /// <summary>
        /// Claim pending, due items of a run so that at most max of its items are running (an attempt is counted).
        /// Concurrent claimers of one run serialize on the run row.
        /// </summary>
        /// <param name="runId">Run identifier.</param>
        /// <param name="workerId">Worker identifier.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="lockUntilUtc">Claim expiry.</param>
        /// <param name="max">Most items of the run running at once (1 to 64), counting items already running.</param>
        /// <param name="includeHarness">Whether items that need a CLI agent harness may be claimed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Claimed items.</returns>
        Task<List<JobItem>> ClaimItemsAsync(string runId, string workerId, DateTime nowUtc, DateTime lockUntilUtc, int max, bool includeHarness, CancellationToken token = default);

        /// <summary>
        /// Items of a run in Running.
        /// </summary>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<int> CountRunningItemsAsync(string runId, CancellationToken token = default);

        /// <summary>
        /// Save the outcome of an attempt when the worker still holds the claim.
        /// </summary>
        /// <param name="item">Item with the new status, result, error, cost, and next attempt time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when saved.</returns>
        /// <exception cref="ArgumentNullException">item is null.</exception>
        Task<bool> CompleteItemAsync(JobItem item, CancellationToken token = default);

        /// <summary>
        /// Return items whose claim expired to Pending, or Dead when no attempt remains.
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Items released.</returns>
        Task<int> ReleaseExpiredItemsAsync(DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Mark the pending items of a run Skipped with a reason.
        /// </summary>
        /// <param name="runId">Run identifier.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Items skipped.</returns>
        Task<int> SkipPendingItemsAsync(string runId, string reason, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Item counts and cost of a run.
        /// </summary>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts.</returns>
        Task<JobItemCounts> CountItemsAsync(string runId, CancellationToken token = default);

        /// <summary>
        /// One item, or null.
        /// </summary>
        /// <param name="id">Item identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Item or null.</returns>
        Task<JobItem?> ReadItemAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Return a Dead or Failed item to Pending with its attempts reset, reopening its run when it had finished.
        /// </summary>
        /// <param name="id">Item identifier.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the item was retried.</returns>
        Task<bool> RetryItemAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Cost of every job item created since a time, in micro-dollars.
        /// </summary>
        /// <param name="sinceUtc">Start.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Micro-dollars.</returns>
        Task<long> CostMicrosSinceAsync(DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate runs.
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="key">Handler key, or null.</param>
        /// <param name="statuses">Statuses (empty for any).</param>
        /// <param name="triggers">Triggers (empty for any).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<JobRun>> EnumerateRunsAsync(EnumerationRequest request, string? key, List<JobRunStatusEnum> statuses, List<JobTriggerEnum> triggers, CancellationToken token = default);

        /// <summary>
        /// Enumerate items.
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="runId">Run identifier, or null.</param>
        /// <param name="key">Handler key, or null.</param>
        /// <param name="statuses">Statuses (empty for any).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<JobItem>> EnumerateItemsAsync(EnumerationRequest request, string? runId, string? key, List<JobItemStatusEnum> statuses, CancellationToken token = default);

        /// <summary>
        /// Runs queued or running.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<int> CountOpenRunsAsync(CancellationToken token = default);

        /// <summary>
        /// The newest run for each of at most 1000 requested keys.
        /// </summary>
        /// <param name="keys">Requested keys (at most 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Runs by key.</returns>
        Task<Dictionary<string, JobRun>> ReadLatestRunsAsync(IReadOnlyCollection<string> keys, CancellationToken token = default);

        /// <summary>
        /// The newest run of a key whose parameters JSON holds a string property with a value (compact JSON, as the
        /// server writes it).
        /// </summary>
        /// <param name="key">Handler key.</param>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Parameter value.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Run or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<JobRun?> ReadLatestRunWithParameterAsync(string key, string name, string value, CancellationToken token = default);

        /// <summary>
        /// Delete finished runs completed before a time, with their items.
        /// </summary>
        /// <param name="beforeUtc">Cutoff.</param>
        /// <param name="maxRuns">Maximum runs per call (1 to 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Runs deleted.</returns>
        Task<int> PruneAsync(DateTime beforeUtc, int maxRuns, CancellationToken token = default);
    }
}
