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
    /// The AI call ledger: one row per model call (docs/INTEGRATION.md). Thread safety: safe for concurrent use.
    /// </summary>
    public interface IAiCallMethods
    {
        /// <summary>
        /// Append a call.
        /// </summary>
        /// <param name="record">Call.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">record is null.</exception>
        Task InsertAsync(AiCallRecord record, CancellationToken token = default);

        /// <summary>
        /// Enumerate calls, newest first.
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<AiCallRecord>> EnumerateAsync(EnumerationRequest request, AiCallFilter filter, CancellationToken token = default);

        /// <summary>
        /// Delete calls older than a time.
        /// </summary>
        /// <param name="beforeUtc">Cutoff.</param>
        /// <param name="max">Maximum rows (1 to 50000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> PruneAsync(DateTime beforeUtc, int max, CancellationToken token = default);

        /// <summary>
        /// Record the tool calls a model call requested and the time the engine spent executing them (known only after
        /// the tools ran).
        /// </summary>
        /// <param name="id">Ledger row id.</param>
        /// <param name="toolCalls">Tool calls executed (0 or more).</param>
        /// <param name="toolExecMs">Total execution time in milliseconds (0 or more).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentException">id is empty.</exception>
        Task SetToolsAsync(string id, int toolCalls, long toolExecMs, CancellationToken token = default);
    }
}
