namespace TrellisKit.Core.Observability
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;
    using System.Linq;

    /// <summary>
    /// Phase 4 directory instruments on the TrellisKit meter (docs/PHASE4_CONTRACT.md section 13). Labels are
    /// low-cardinality only (never ids, names, or coordinates); the property label is added by TrellisKitTelemetry.
    /// Thread safety: instruments are thread-safe; the pending gauge reads a concurrent dictionary.
    /// </summary>
    public static class DirectoryTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Venue writes, labeled action (create, update, delete, verify, close, archive) and outcome.
        /// </summary>
        public static readonly Counter<long> Venues = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.directory.venues", "{venue}", "Directory venue writes.");

        /// <summary>
        /// Venue review writes, labeled action (create, update, delete, reply, reply_delete) and outcome (ok, rejected, conflict, filtered, limited).
        /// </summary>
        public static readonly Counter<long> Reviews = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.directory.reviews", "{review}", "Venue review writes.");

        /// <summary>
        /// Venue member media writes, labeled action (add, delete, remove), kind (image, video, none), and outcome.
        /// </summary>
        public static readonly Counter<long> VenueMedia = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.directory.venue_media", "{item}", "Venue member media writes.");

        /// <summary>
        /// Submissions, labeled type, action (submit, approve, reject, withdraw), and outcome.
        /// </summary>
        public static readonly Counter<long> Submissions = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.directory.submissions", "{submission}", "Directory submissions.");

        /// <summary>
        /// Pending submissions at the last count, per property.
        /// </summary>
        public static readonly ObservableGauge<long> SubmissionsPending = TrellisKitTelemetry.Meter.CreateObservableGauge<long>("trelliskit.directory.submissions.pending", ObservePending, "{submission}", "Pending directory submissions.");

        /// <summary>
        /// Geocoder calls, labeled provider, outcome (hit, cache_hit, miss, error), and purpose (write, search).
        /// </summary>
        public static readonly Counter<long> Geocode = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.directory.geocode", "{call}", "Geocoder lookups.");

        /// <summary>
        /// Geocoder call duration in seconds, labeled provider.
        /// </summary>
        public static readonly Histogram<double> GeocodeDuration = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.directory.geocode.duration", "s", "Geocoder call duration.");

        /// <summary>
        /// Map enumerations, labeled mode (points, clusters).
        /// </summary>
        public static readonly Counter<long> MapQueries = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.directory.map.queries", "{query}", "Map enumerations.");

        /// <summary>
        /// Spatial and search query duration in seconds, labeled kind (near, bbox, map, search).
        /// </summary>
        public static readonly Histogram<double> QueryDuration = TrellisKitTelemetry.Meter.CreateHistogram<double>("trelliskit.directory.query.duration", "s", "Directory query duration.");

        /// <summary>
        /// Tile proxy requests, labeled outcome (proxied, cache_hit, error).
        /// </summary>
        public static readonly Counter<long> Tiles = TrellisKitTelemetry.Meter.CreateCounter<long>("trelliskit.directory.tiles", "{tile}", "Map tile proxy requests.");

        #endregion

        #region Private-Members

        private static readonly ConcurrentDictionary<string, long> _Pending = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record the pending submission count of a property.
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <param name="count">Pending submissions.</param>
        public static void SetPending(string propertyId, long count)
        {
            if (String.IsNullOrEmpty(propertyId)) return;
            _Pending[propertyId] = Math.Max(0, count);
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<Measurement<long>> ObservePending()
        {
            return _Pending.Select(p => new Measurement<long>(p.Value, new KeyValuePair<string, object?>("trelliskit.property", p.Key))).ToList();
        }

        #endregion
    }
}
