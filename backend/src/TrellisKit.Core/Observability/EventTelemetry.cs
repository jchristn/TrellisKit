namespace TrellisKit.Core.Observability
{
    using System;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Phase 4 event instruments on the TrellisKit meter (docs/PHASE4_CONTRACT.md section 13), all with the
    /// trelliskit.property label. Labels are low-cardinality only (never ids, titles, or coordinates).
    /// Thread safety: instruments are thread-safe.
    /// </summary>
    public static class EventTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Event writes, labeled action (create, publish, update, cancel, duplicate, delete, propose, approve, reject, withdraw).
        /// </summary>
        public static readonly Counter<long> Events = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.events.events", "{event}", "Event writes.");

        /// <summary>
        /// RSVP writes, labeled action (going, interested, waitlisted, remove, promote, check_in) and outcome.
        /// </summary>
        public static readonly Counter<long> Rsvps = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.events.rsvps", "{rsvp}", "RSVP writes.");

        /// <summary>
        /// Reminders, labeled outcome (sent, skipped_late) and channel (inapp, email).
        /// </summary>
        public static readonly Counter<long> Reminders = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.events.reminders", "{reminder}", "Event reminders.");

        /// <summary>
        /// Calendar downloads, labeled kind (ics, series, feed) and outcome.
        /// </summary>
        public static readonly Counter<long> Calendar = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.events.calendar", "{download}", "ICS downloads and calendar feed fetches.");

        /// <summary>
        /// Recurrence occurrences materialized or changed, labeled action (materialize, regenerate, cancel).
        /// </summary>
        public static readonly Counter<long> Occurrences = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.events.occurrences", "{occurrence}", "Recurring event occurrences.");

        /// <summary>
        /// Events map enumerations, labeled mode (points, clusters).
        /// </summary>
        public static readonly Counter<long> MapQueries = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.events.map.queries", "{query}", "Events map enumerations.");

        /// <summary>
        /// Events map query duration.
        /// </summary>
        public static readonly Histogram<double> MapDuration = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.events.map.duration", "s", "Events map query duration.");

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add to a counter with label pairs (name, value, name, value, ...).
        /// </summary>
        /// <param name="counter">Counter.</param>
        /// <param name="amount">Amount.</param>
        /// <param name="labels">Label names and values.</param>
        /// <exception cref="ArgumentNullException">The counter is null.</exception>
        public static void Count(Counter<long> counter, long amount, params string[] labels)
        {
            ArgumentNullException.ThrowIfNull(counter);
            if (amount <= 0) return;
            TrellisKitTelemetry.Add(counter, amount, labels);
        }

        #endregion
    }
}
