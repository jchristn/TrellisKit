namespace TrellisKit.Core.Requests
{
    using TrellisKit.Core.Metrics;
    /// <summary>Standard paging/filtering over the actual metric observation rows.</summary>
    public sealed class MetricEnumerationQuery : EnumerationQuery
    {
        /// <summary>Metric selection and bounded source window; the whole selection is bound into cursors.</summary>
        public MetricQuery Query { get; set; } = new MetricQuery { Metric="requests.count" };
    }
}
