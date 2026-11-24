namespace TrellisKit.Core.Observability
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Support ticket instruments (docs/PHASE5_CONTRACT.md section 15): ticket actions, first response time, and the open
    /// and overdue gauges per property (refreshed by the ticket due worker). Labels are low-cardinality; never ids or text.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public static class SupportTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Ticket actions, labeled by action (create, reply, note, status, assign), type (the configured key, bounded by the
        /// type list), and actor (member, staff, system).
        /// </summary>
        public static readonly Counter<long> Tickets = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.support.tickets", "{action}", "Support ticket actions.");

        /// <summary>
        /// Hours from ticket creation to the first staff reply.
        /// </summary>
        public static readonly Histogram<double> FirstResponseHours = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.support.first_response.hours", "h", "Hours to the first staff reply.");

        /// <summary>
        /// Tickets that are not Closed, per property.
        /// </summary>
        public static readonly ObservableGauge<long> Open = TrellisKitTelemetry.Meter.CreateObservableGauge<long>("trelliskit.support.tickets.open", OpenMeasurements, "{ticket}", "Support tickets not Closed.");

        /// <summary>
        /// Tickets past the first response target, per property.
        /// </summary>
        public static readonly ObservableGauge<long> Overdue = TrellisKitTelemetry.Meter.CreateObservableGauge<long>("trelliskit.support.tickets.overdue", OverdueMeasurements, "{ticket}", "Support tickets past the first response target.");

        #endregion

        #region Private-Members

        private static readonly ConcurrentDictionary<string, long> _Open = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, long> _Overdue = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Count one ticket action.
        /// </summary>
        /// <param name="action">create, reply, note, status, or assign.</param>
        /// <param name="type">Ticket type key.</param>
        /// <param name="actor">member, staff, or system.</param>
        public static void Count(string action, string type, string actor)
        {
            TrellisKitTelemetry.Add(Tickets, 1, "action", action ?? "unknown", "type", String.IsNullOrEmpty(type) ? "unknown" : type, "actor", actor ?? "unknown");
        }

        /// <summary>
        /// Record a first response time.
        /// </summary>
        /// <param name="hours">Hours.</param>
        public static void RecordFirstResponse(double hours)
        {
            TrellisKitTelemetry.Record(FirstResponseHours, Math.Max(0, hours));
        }

        /// <summary>
        /// Set the open and overdue gauges of the current property (the worker pass's property).
        /// </summary>
        /// <param name="open">Tickets not Closed.</param>
        /// <param name="overdue">Tickets overdue.</param>
        public static void SetGauges(long open, long overdue)
        {
            string property = TrellisKitTelemetry.PropertyValue();
            _Open[property] = Math.Max(0, open);
            _Overdue[property] = Math.Max(0, overdue);
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<Measurement<long>> OpenMeasurements()
        {
            foreach (KeyValuePair<string, long> entry in _Open)
                yield return new Measurement<long>(entry.Value, new KeyValuePair<string, object?>(TrellisKitTelemetry.PropertyLabel, entry.Key));
        }

        private static IEnumerable<Measurement<long>> OverdueMeasurements()
        {
            foreach (KeyValuePair<string, long> entry in _Overdue)
                yield return new Measurement<long>(entry.Value, new KeyValuePair<string, object?>(TrellisKitTelemetry.PropertyLabel, entry.Key));
        }

        #endregion
    }
}
