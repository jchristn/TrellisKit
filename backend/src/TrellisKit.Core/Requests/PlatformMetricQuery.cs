namespace TrellisKit.Core.Requests
{
    using TrellisKit.Core.Metrics;

    /// <summary>Property comparison query: enumeration filters select properties, Query selects their registered metric.</summary>
    public class PlatformMetricQuery : EnumerationQuery
    {
        /// <summary>Metric query, with at most 2000 buckets. GroupBy must be null for property rankings.</summary>
        public MetricQuery Query { get; set; } = new MetricQuery();
        /// <summary>Include each returned property's series for the Health view. Default false; at most 20 properties.</summary>
        public bool IncludeSeries { get; set; } = false;
    }
}
