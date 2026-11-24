namespace TrellisKit.Core.PropertyConfig
{
    using System.Collections.Generic;
    using TrellisKit.Core.Metrics;

    /// <summary>
    /// Property-owned dashboard declarations. Product events are disabled unless explicitly enabled.
    /// Collection limits are validated when the metrics registry is constructed.
    /// </summary>
    public sealed class PropertyMetricsConfiguration
    {
        /// <summary>Thresholds for the seven default operational alerts.</summary>
        public MetricAlertThresholds AlertThresholds { get; set; } = new MetricAlertThresholds();
        /// <summary>Whether the aggregate metrics worker runs. Default true.</summary>
        public bool Enabled { get; set; } = true;
        /// <summary>Product event collection consent/configuration. Default false.</summary>
        public bool ProductEventsEnabled { get; set; } = false;
        /// <summary>Aggregate retention in days, 1 to 1830. Default 180.</summary>
        public int RetentionDays { get; set; } = 180;
        /// <summary>Worker detail retention in days, 1 to 365. Default 30.</summary>
        public int WorkerRetentionDays { get; set; } = 30;
        /// <summary>Additional metrics (at most 100); cannot replace engine metrics.</summary>
        public List<MetricDefinition> Definitions { get; set; } = new List<MetricDefinition>();
        /// <summary>Additional product events (at most 100).</summary>
        public List<MetricEventDeclaration> Events { get; set; } = new List<MetricEventDeclaration>();
        /// <summary>Additional pages or charts on engine pages (at most 20 pages).</summary>
        public List<MetricPageDeclaration> Pages { get; set; } = new List<MetricPageDeclaration>();
        /// <summary>Additional threshold alerts (at most 100).</summary>
        public List<MetricAlertRule> Alerts { get; set; } = new List<MetricAlertRule>();
        /// <summary>Whether to evaluate and deliver metric alerts. Default true.</summary>
        public bool AlertsEnabled { get; set; } = true;
        /// <summary>Optional storage quota in bytes; null disables projection alerts.</summary>
        public long? StorageQuotaBytes { get; set; } = null;
    }
}
