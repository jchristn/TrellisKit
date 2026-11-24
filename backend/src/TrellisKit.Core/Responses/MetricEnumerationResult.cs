namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Metrics;
    /// <summary>Standard observation enumeration with scalar chart context, also available for an empty page.</summary>
    public sealed class MetricEnumerationResult : EnumerationResult<MetricObservationRow>
    {
        /// <summary>Metric key.</summary>
        public string Metric { get; set; } = String.Empty;
        /// <summary>Unit.</summary>
        public string Unit { get; set; } = String.Empty;
        /// <summary>Property or operational calendar.</summary>
        public string TimeZone { get; set; } = "UTC";
        /// <summary>Complete rollup watermark.</summary>
        public DateTime? CompleteThroughUtc { get; set; }
        /// <summary>Copy the shared enumeration metadata without embedding the source collection.</summary>
        public static MetricEnumerationResult From(EnumerationResult<MetricObservationRow> page, MetricResult source) => new MetricEnumerationResult
        {
            Success = page.Success,
            MaxResults = page.MaxResults,
            Skip = page.Skip,
            ContinuationToken = page.ContinuationToken,
            EndOfResults = page.EndOfResults,
            TotalRecords = page.TotalRecords,
            RecordsRemaining = page.RecordsRemaining,
            TimestampUtc = page.TimestampUtc,
            Objects = page.Objects,
            Metric = source.Metric,
            Unit = source.Unit,
            TimeZone = source.TimeZone,
            CompleteThroughUtc = source.CompleteThroughUtc
        };
    }
}
