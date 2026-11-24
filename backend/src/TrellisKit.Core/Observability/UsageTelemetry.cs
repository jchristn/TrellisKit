namespace TrellisKit.Core.Observability
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;
    using System.Linq;

    /// <summary>
    /// Usage metering instruments on the TrellisKit meter (docs/USAGE_METERING_CONTRACT.md section 9). Labels are
    /// low-cardinality only: the property label (bounded by deployment.maxProperties) and the storage class.
    /// Thread safety: instruments are thread-safe; gauges read concurrent dictionaries.
    /// </summary>
    public static class UsageTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Rollup passes, labeled outcome (ok, skipped, error).
        /// </summary>
        public static readonly Counter<long> Passes = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.usage.passes", "{pass}", "Usage rollup passes.");

        /// <summary>
        /// Rollup pass duration in seconds.
        /// </summary>
        public static readonly Histogram<double> PassDuration = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.usage.pass.duration", "s", "Usage rollup pass duration.");

        /// <summary>
        /// Rollup rows written, labeled granularity (hour, day).
        /// </summary>
        public static readonly Counter<long> Rows = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.usage.rows", "{row}", "Usage rollup rows written.");

        /// <summary>
        /// Local days backfilled.
        /// </summary>
        public static readonly Counter<long> BackfillDays = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.usage.backfill.days", "{day}", "Usage days backfilled.");

        /// <summary>
        /// Storage objects measured or reconciled, labeled outcome (ok, missing).
        /// </summary>
        public static readonly Counter<long> StorageMeasured = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.usage.storage.measured", "{object}", "Storage objects measured or reconciled.");

        /// <summary>
        /// Usage exports served, labeled format (csv, json).
        /// </summary>
        public static readonly Counter<long> Exports = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.usage.exports", "{export}", "Usage exports served.");

        /// <summary>
        /// Latest storage gauge per property and class.
        /// </summary>
        public static readonly ObservableGauge<long> StorageBytes = TrellisKitTelemetry.Meter.CreateObservableGauge<long>("trelliskit.usage.storage.bytes", ObserveStorage, "By", "Stored bytes per property and storage class.");

        /// <summary>
        /// Latest member total per property.
        /// </summary>
        public static readonly ObservableGauge<long> UsersTotal = TrellisKitTelemetry.Meter.CreateObservableGauge<long>("trelliskit.usage.users.total", ObserveUsers, "{member}", "Members per property.");

        /// <summary>
        /// Seconds since each property's last completed rollup.
        /// </summary>
        public static readonly ObservableGauge<double> LagSeconds = TrellisKitTelemetry.Meter.CreateObservableGauge<double>("trelliskit.usage.lag.seconds", ObserveLag, "s", "Seconds since the last usage rollup per property.");

        #endregion

        #region Private-Members

        private static readonly ConcurrentDictionary<string, long> _Storage = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, long> _Users = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, DateTime> _LastRollup = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record the latest gauges of a property after a rollup pass.
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <param name="storageByClass">Bytes per storage meter id.</param>
        /// <param name="usersTotal">Member total.</param>
        /// <param name="completedUtc">Pass completion time.</param>
        /// <exception cref="ArgumentNullException">The property id or the storage map is null.</exception>
        public static void SetLatest(string propertyId, IReadOnlyDictionary<string, long> storageByClass, long usersTotal, DateTime completedUtc)
        {
            if (String.IsNullOrEmpty(propertyId)) throw new ArgumentNullException(nameof(propertyId));
            ArgumentNullException.ThrowIfNull(storageByClass);
            foreach (KeyValuePair<string, long> pair in storageByClass) _Storage[propertyId + "|" + pair.Key] = pair.Value;
            _Users[propertyId] = usersTotal;
            _LastRollup[propertyId] = completedUtc;
        }

        /// <summary>
        /// Add to a counter with label pairs (name, value, ...).
        /// </summary>
        /// <param name="counter">Counter.</param>
        /// <param name="amount">Amount.</param>
        /// <param name="labels">Label names and values.</param>
        public static void Add(Counter<long> counter, long amount, params string[] labels)
        {
            TrellisKitTelemetry.Add(counter, amount, labels);
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<Measurement<long>> ObserveStorage()
        {
            return _Storage.Select(pair =>
            {
                int bar = pair.Key.IndexOf('|');
                return new Measurement<long>(pair.Value, new KeyValuePair<string, object?>("trelliskit.property", pair.Key.Substring(0, bar)), new KeyValuePair<string, object?>("class", pair.Key.Substring(bar + 1)));
            }).ToList();
        }

        private static IEnumerable<Measurement<long>> ObserveUsers()
        {
            return _Users.Select(pair => new Measurement<long>(pair.Value, new KeyValuePair<string, object?>("trelliskit.property", pair.Key))).ToList();
        }

        private static IEnumerable<Measurement<double>> ObserveLag()
        {
            DateTime now = DateTime.UtcNow;
            return _LastRollup.Select(pair => new Measurement<double>(Math.Max(0, (now - pair.Value).TotalSeconds), new KeyValuePair<string, object?>("trelliskit.property", pair.Key))).ToList();
        }

        #endregion
    }
}
