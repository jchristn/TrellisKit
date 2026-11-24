namespace TrellisKit.Core.Metrics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Declarative metric contract; no property-specific identifiers or executable queries.
    /// </summary>
    public sealed class MetricDefinition
    {
        /// <summary>Inline labels (empty for immutable operational records).</summary>
        public List<string> Labels { get; set; } = new List<string>();
        /// <summary>Inline tags (empty for immutable operational records).</summary>
        public Dictionary<string,string> Tags { get; set; } = new Dictionary<string,string>();
        /// <summary>
        /// Stable metric key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Human-readable title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Unit: count, percent, ms, bytes, tokens, usdMicros.
        /// </summary>
        public string Unit { get; set; } = "count";

        /// <summary>
        /// Count, Gauge, Distinct, or Distribution.
        /// </summary>
        public string Kind { get; set; } = "Count";

        /// <summary>
        /// Usage, Recorded, Snapshot, Requests, AI, or Events.
        /// </summary>
        public string Source { get; set; } = "Recorded";

        /// <summary>
        /// Allowed dimension names.
        /// </summary>
        public List<string> Groupings { get; set; } = new List<string>();

        /// <summary>
        /// Allowed bucket sizes.
        /// </summary>
        public List<string> Buckets { get; set; } = new List<string> { "Hour", "Day", "Week" };

        /// <summary>
        /// Retention in days, 1 to 1830.
        /// </summary>
        public int RetentionDays { get; set; } = 180;

        /// <summary>
        /// Role required to query the metric.
        /// </summary>
        public string ReadRole { get; set; } = "SystemAdmin";

        /// <summary>
        /// Suppress grouped counts smaller than five.
        /// </summary>
        public bool MemberSensitive { get; set; } = false;

        /// <summary>
        /// Optional engine feature flag.
        /// </summary>
        public string? Feature { get; set; } = null;

        /// <summary>
        /// Local admin path for the source records.
        /// </summary>
        public string? Drilldown { get; set; } = null;

    }
}
