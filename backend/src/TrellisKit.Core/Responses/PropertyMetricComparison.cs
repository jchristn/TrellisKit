namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Metrics;

    /// <summary>A property-scoped metric comparison. Missing or private values remain null.</summary>
    public class PropertyMetricComparison
    {
        /// <summary>Property identifier.</summary>
        public string PropertyId { get; set; } = String.Empty;
        /// <summary>Property display name.</summary>
        public string Name { get; set; } = String.Empty;
        /// <summary>Registered metric key.</summary>
        public string Metric { get; set; } = String.Empty;
        /// <summary>Metric unit.</summary>
        public string Unit { get; set; } = String.Empty;
        /// <summary>Sum for counts, Last for gauges, WorstBucket for distributions. Percentiles are never summed or averaged.</summary>
        public string Aggregation { get; set; } = String.Empty;
        /// <summary>Comparison value, null for a missing or suppressed series.</summary>
        public double? Value { get; set; } = null;
        /// <summary>Whether all returned buckets are complete.</summary>
        public bool Complete { get; set; } = false;
        /// <summary>Latest fully rolled-up time.</summary>
        public DateTime? CompleteThroughUtc { get; set; } = null;
        /// <summary>Reason the property has no metric (for example MetricNotRegistered).</summary>
        public string? UnavailableReason { get; set; } = null;
        /// <summary>Optional chart series, limited to the returned page.</summary>
        public MetricEnumerationResult? Data { get; set; } = null;
        /// <summary>Property labels.</summary>
        public List<string> Labels { get; set; } = new List<string>();
        /// <summary>Property tags.</summary>
        public Dictionary<string,string> Tags { get; set; } = new Dictionary<string,string>();
    }
}
