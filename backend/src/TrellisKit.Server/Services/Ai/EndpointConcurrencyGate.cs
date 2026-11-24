namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;

    /// <summary>
    /// Per-endpoint concurrency and bounded queue (docs/AI_CONTRACT.md section 3.6): at most maxConcurrentRequests calls
    /// in flight per endpoint and API task, and at most maxQueueDepth waiting; a call that cannot queue fails at once
    /// with 503 ModelBusy (Retry-After 5). Queue waits are measured.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class EndpointConcurrencyGate
    {
        #region Private-Members

        private readonly ConcurrentDictionary<string, EndpointSlot> _Slots = new ConcurrentDictionary<string, EndpointSlot>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Wait for a slot.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A lease that frees the slot when disposed.</returns>
        /// <exception cref="ArgumentNullException">The endpoint is null.</exception>
        /// <exception cref="Core.Exceptions.ApiErrorException">503 ModelBusy when the queue is full.</exception>
        public async Task<IDisposable> EnterAsync(ModelEndpoint endpoint, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            int concurrency = Math.Clamp(endpoint.MaxConcurrentRequests, 1, 256);
            EndpointSlot slot = _Slots.AddOrUpdate(endpoint.Id, id => new EndpointSlot(concurrency), (id, existing) => existing.Concurrency == concurrency ? existing : new EndpointSlot(concurrency));
            string apiType = AiTelemetry.Label(endpoint.ApiType.ToString());
            if (slot.Semaphore.Wait(0)) return new SlotLease(slot);
            int waiting = slot.EnterQueue();
            try
            {
                if (waiting > Math.Max(0, endpoint.MaxQueueDepth))
                {
                    TrellisKitTelemetry.Add(AiTelemetry.QueueRejected, 1, "api_type", apiType);
                    throw AiErrors.ModelBusy();
                }
                Stopwatch watch = Stopwatch.StartNew();
                await slot.Semaphore.WaitAsync(token).ConfigureAwait(false);
                TrellisKitTelemetry.Record(AiTelemetry.QueueWait, watch.Elapsed.TotalSeconds, "api_type", apiType);
                return new SlotLease(slot);
            }
            finally
            {
                slot.LeaveQueue();
            }
        }

        #endregion
    }
}
