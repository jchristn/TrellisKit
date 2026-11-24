namespace TrellisKit.Core.Metrics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Bounded metrics query. At most 2000 time buckets and 20 groups plus Other.
    /// </summary>
    public sealed class MetricQuery
    {
        /// <summary>
        /// Registered metric key.
        /// </summary>
        public string Metric { get; set; } = String.Empty;

        /// <summary>
        /// Inclusive UTC start.
        /// </summary>
        public DateTime From { get; set; } = DateTime.UtcNow.AddDays(-7);

        /// <summary>
        /// Exclusive UTC end.
        /// </summary>
        public DateTime To { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Hour, Day, or Week.
        /// </summary>
        public string Bucket { get; set; } = "Day";

        /// <summary>
        /// Allowed dimension, or null for total.
        /// </summary>
        public string? GroupBy { get; set; } = null;

        /// <summary>
        /// Exact filters on declared dimensions.
        /// </summary>
        public Dictionary<string,string> Filters { get; set; } = new Dictionary<string,string>(StringComparer.Ordinal);

        /// <summary>
        /// Include the preceding interval of equal duration.
        /// </summary>
        public bool Compare { get; set; } = true;

        /// <summary>
        /// Distribution percentile in (0,1].
        /// </summary>
        public double Percentile { get; set; } = 0.95;

    }
}
