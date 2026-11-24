namespace TrellisKit.Core.Metrics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Bounded metrics response; values are never fabricated for missing buckets.
    /// </summary>
    public sealed class MetricResult
    {
        /// <summary>
        /// Metric key.
        /// </summary>
        public string Metric { get; set; } = String.Empty;

        /// <summary>
        /// Unit.
        /// </summary>
        public string Unit { get; set; } = String.Empty;

        /// <summary>
        /// Display calendar.
        /// </summary>
        public string TimeZone { get; set; } = "UTC";

        /// <summary>
        /// Latest fully rolled-up time.
        /// </summary>
        public DateTime? CompleteThroughUtc { get; set; } = null;

        /// <summary>
        /// At most 20 groups plus Other.
        /// </summary>
        public List<MetricSeries> Series { get; set; } = new List<MetricSeries>();

        /// <summary>
        /// Previous comparison period.
        /// </summary>
        public List<MetricSeries> Previous { get; set; } = new List<MetricSeries>();

        /// <summary>
        /// At most 200 marked moments.
        /// </summary>
        public List<MetricAnnotation> Annotations { get; set; } = new List<MetricAnnotation>();

    }
}
