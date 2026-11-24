namespace TrellisKit.Core.Metrics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An aggregate observation from trusted server code; never contains member text.
    /// </summary>
    public sealed class MetricObservation
    {
        /// <summary>
        /// Registered metric key.
        /// </summary>
        public string Metric { get; set; } = String.Empty;

        /// <summary>
        /// UTC measurement time.
        /// </summary>
        public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Measured value.
        /// </summary>
        public double Value { get; set; } = 0;

        /// <summary>
        /// Declared dimension values only.
        /// </summary>
        public Dictionary<string,string> Dimensions { get; set; } = new Dictionary<string,string>(StringComparer.Ordinal);

        /// <summary>
        /// Number of observations represented.
        /// </summary>
        public long Samples { get; set; } = 1;

    }
}
