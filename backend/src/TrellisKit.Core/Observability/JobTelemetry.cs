namespace TrellisKit.Core.Observability
{
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Job engine metrics (trelliskit.jobs.*; Grafana queries trelliskit_jobs_*).
    /// </summary>
    public static class JobTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Runs by key and outcome (queued, skipped_active, skipped_budget, succeeded, partially_succeeded, failed, cancelled).
        /// </summary>
        public static readonly Counter<long> Runs = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.jobs.runs", "{run}", "Job runs by outcome.");

        /// <summary>
        /// Item attempts by key and outcome (succeeded, retry, dead, failed).
        /// </summary>
        public static readonly Counter<long> Items = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.jobs.items", "{attempt}", "Job item attempts by outcome.");

        /// <summary>
        /// Item attempt duration in milliseconds by key.
        /// </summary>
        public static readonly Histogram<double> ItemDuration = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.jobs.item.duration", "ms", "Job item attempt duration.");

        /// <summary>
        /// Job cost in micro-dollars by key.
        /// </summary>
        public static readonly Counter<long> CostMicros = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.jobs.cost", "{microdollar}", "Job cost in micro-dollars.");

        #endregion
    }
}
