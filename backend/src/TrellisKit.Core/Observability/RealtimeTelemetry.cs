namespace TrellisKit.Core.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;
    using System.Threading;

    /// <summary>
    /// Realtime WebSocket and bus instruments on the TrellisKit meter (docs/PHASE5_CONTRACT.md section 15). Labels are
    /// low cardinality only (never ids, names, or text). Thread safety: instruments are thread safe; the queue usage
    /// gauge reads an atomically updated field.
    /// </summary>
    public static class RealtimeTelemetry
    {
        #region Public-Members

        /// <summary>
        /// trelliskit.realtime.connections: open authenticated connections (client web, mobile, other).
        /// </summary>
        public static readonly UpDownCounter<long> Connections = TrellisKitTelemetry.Meter.CreateUpDownCounter<long>("trelliskit.realtime.connections", "{connection}", "Open realtime connections.");

        /// <summary>
        /// trelliskit.realtime.connects (outcome ok, auth_failed, timeout, rejected_origin, over_capacity, rate_limited, disabled, protocol).
        /// </summary>
        public static readonly Counter<long> Connects = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.realtime.connects", "{connect}", "Realtime connection attempts by outcome.");

        /// <summary>
        /// trelliskit.realtime.closes (code).
        /// </summary>
        public static readonly Counter<long> Closes = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.realtime.closes", "{close}", "Realtime connection closes by code.");

        /// <summary>
        /// trelliskit.realtime.frames (direction in or out, type).
        /// </summary>
        public static readonly Counter<long> Frames = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.realtime.frames", "{frame}", "Realtime frames by direction and type.");

        /// <summary>
        /// trelliskit.realtime.dropped (reason overflow, slow_consumer, rate_limited).
        /// </summary>
        public static readonly Counter<long> Dropped = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.realtime.dropped", "{frame}", "Realtime frames dropped by reason.");

        /// <summary>
        /// trelliskit.realtime.resyncs (reason Gap, Overflow, BusReconnected, Resume).
        /// </summary>
        public static readonly Counter<long> Resyncs = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.realtime.resyncs", "{resync}", "resync.required frames sent by reason.");

        /// <summary>
        /// trelliskit.realtime.bus.events (kind, outcome delivered_local or no_local_recipients).
        /// </summary>
        public static readonly Counter<long> BusEvents = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.realtime.bus.events", "{event}", "Realtime bus events received by kind and outcome.");

        /// <summary>
        /// trelliskit.realtime.bus.lag in seconds (publish to receive).
        /// </summary>
        public static readonly Histogram<double> BusLag = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.realtime.bus.lag", "s", "Realtime bus lag from publish to receive.");

        /// <summary>
        /// trelliskit.realtime.bus.reconnects.
        /// </summary>
        public static readonly Counter<long> BusReconnects = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.realtime.bus.reconnects", "{reconnect}", "Realtime bus listener reconnects.");

        /// <summary>
        /// trelliskit.realtime.bus.queue_usage: pg_notification_queue_usage() as a fraction from 0 to 1 (alarm at 0.5).
        /// </summary>
        public static readonly ObservableGauge<double> BusQueueUsage = TrellisKitTelemetry.Meter.CreateObservableGauge<double>("trelliskit.realtime.bus.queue_usage", () => Volatile.Read(ref _QueueUsage), "1", "PostgreSQL notification queue usage.");

        #endregion

        #region Private-Members

        private static double _QueueUsage = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add one to a counter with label pairs (name, value, name, value, ...). Never throws.
        /// </summary>
        /// <param name="counter">Counter.</param>
        /// <param name="labels">Label names and values.</param>
        public static void Count(Counter<long> counter, params string[] labels)
        {
            TrellisKitTelemetry.Add(counter, 1, labels);
        }

        /// <summary>
        /// Change the open connection gauge for a client kind (with the current property label). Never throws.
        /// </summary>
        /// <param name="client">web, mobile, or other.</param>
        /// <param name="delta">+1 or -1.</param>
        public static void ChangeConnections(string client, long delta)
        {
            try
            {
                Connections.Add(delta, new KeyValuePair<string, object?>("client", client), new KeyValuePair<string, object?>(TrellisKitTelemetry.PropertyLabel, TrellisKitTelemetry.PropertyValue()));
            }
            catch (InvalidOperationException)
            {
                // Telemetry is best-effort and never affects the caller.
            }
        }

        /// <summary>
        /// Set the notification queue usage gauge.
        /// </summary>
        /// <param name="usage">Fraction from 0 to 1.</param>
        public static void SetQueueUsage(double usage)
        {
            Volatile.Write(ref _QueueUsage, Math.Clamp(usage, 0, 1));
        }

        #endregion
    }
}
