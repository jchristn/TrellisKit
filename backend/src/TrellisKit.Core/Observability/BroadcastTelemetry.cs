namespace TrellisKit.Core.Observability
{
    using System;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Phase 5 broadcast instruments on the TrellisKit meter (docs/PHASE5_CONTRACT.md section 15). Labels are
    /// low-cardinality only (never ids, names, or text). Thread safety: instruments are thread-safe.
    /// </summary>
    public static class BroadcastTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Broadcasts that started sending, one per channel used, labeled channel (in_app, push, email) and category
        /// (optional, essential).
        /// </summary>
        public static readonly Counter<long> Sent = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.broadcasts.sent", "{broadcast}", "Broadcasts that started sending, per channel.");

        /// <summary>
        /// Per recipient channel outcomes, labeled channel (in_app, push, email) and outcome (created, queued,
        /// skipped_preference, no_token, no_address, suppressed, failed).
        /// </summary>
        public static readonly Counter<long> Deliveries = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.broadcasts.deliveries", "{delivery}", "Broadcast deliveries per channel and outcome.");

        #endregion
    }
}
