namespace TrellisKit.Server.Services.Jobs
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A background job (docs/INTEGRATION.md). The engine calls <see cref="PlanAsync"/> once per run to list
    /// the run's items, then <see cref="ExecuteAsync"/> per item, with retries, backoff, a dead-letter state, budgets,
    /// and per-run concurrency. Implementations must be safe for concurrent ExecuteAsync calls and idempotent per item
    /// (an item can be retried after a worker stops mid-attempt).
    /// </summary>
    public interface IJobHandler
    {
        /// <summary>
        /// Stable key (for example discovery.organizations), unique per property.
        /// </summary>
        string Key { get; }

        /// <summary>
        /// Defaults for the job's definition, used when the definition is first created.
        /// </summary>
        JobHandlerDefaults Defaults { get; }

        /// <summary>
        /// List the items of a run (at most <see cref="JobService.MaxItemsPerRun"/>; target keys unique within the run).
        /// </summary>
        /// <param name="context">Run context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Items.</returns>
        Task<List<JobItemPlan>> PlanAsync(JobRunContext context, CancellationToken token);

        /// <summary>
        /// Execute one item. Return <see cref="JobItemOutcome.Ok"/> or <see cref="JobItemOutcome.Fail"/>; an exception
        /// counts as a retryable failure. In a dry run, do not write lasting changes.
        /// </summary>
        /// <param name="context">Item context.</param>
        /// <param name="token">Cancellation token (cancelled when the claim would expire or on shutdown).</param>
        /// <returns>Outcome.</returns>
        Task<JobItemOutcome> ExecuteAsync(JobItemContext context, CancellationToken token);
    }
}
