namespace TrellisKit.Core.Observability
{
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Training hub instruments on the TrellisKit meter (docs/TRELLISKIT.md section 15). Labels are low-cardinality only
    /// (outcome, provider name, kind), never ids or text. Thread safety: instruments are thread-safe.
    /// </summary>
    public static class TrainingTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Span name of one overview request.
        /// </summary>
        public const string OverviewSpan = "training.overview";

        /// <summary>
        /// Overview requests, labeled outcome (success, not_available, error).
        /// </summary>
        public static readonly Counter<long> OverviewRequests = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.training.overview.requests", "{request}", "Training overview requests, by outcome.");

        /// <summary>
        /// Overview duration in seconds, labeled outcome.
        /// </summary>
        public static readonly Histogram<double> OverviewDuration = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.training.overview.duration", "s", "Training overview duration.");

        /// <summary>
        /// Section provider failures, labeled provider (library, saves, events, rank, coach, or a property provider).
        /// </summary>
        public static readonly Counter<long> ProviderErrors = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.training.provider.errors", "{error}", "Training overview section provider failures.");

        /// <summary>
        /// Unified library searches, labeled outcome (success, error).
        /// </summary>
        public static readonly Counter<long> Searches = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.library.search.requests", "{search}", "Unified library searches, by outcome.");

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add one to a counter with label pairs (name, value, name, value, ...).
        /// </summary>
        /// <param name="counter">Counter.</param>
        /// <param name="labels">Label names and values.</param>
        /// <exception cref="ArgumentNullException">The counter is null.</exception>
        public static void Count(Counter<long> counter, params string[] labels)
        {
            ArgumentNullException.ThrowIfNull(counter);
            TrellisKitTelemetry.Add(counter, 1, labels);
        }

        /// <summary>
        /// Start the overview span. Returns null when nobody is listening.
        /// </summary>
        /// <returns>Activity, or null.</returns>
        public static Activity? StartOverview()
        {
            return TrellisKitTelemetry.StartSpan(OverviewSpan);
        }

        #endregion
    }
}
