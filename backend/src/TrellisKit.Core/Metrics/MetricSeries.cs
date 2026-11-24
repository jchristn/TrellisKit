namespace TrellisKit.Core.Metrics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A single bounded series.
    /// </summary>
    public sealed class MetricSeries
    {
        /// <summary>
        /// Group value.
        /// </summary>
        public string Key { get; set; } = "total";

        /// <summary>
        /// At most 2000 points.
        /// </summary>
        public List<MetricPoint> Points { get; set; } = new List<MetricPoint>();

    }
}
