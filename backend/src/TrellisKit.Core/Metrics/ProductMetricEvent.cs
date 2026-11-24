namespace TrellisKit.Core.Metrics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Privacy-safe product event payload; no content or caller-supplied member id.
    /// </summary>
    public sealed class ProductMetricEvent
    {
        /// <summary>
        /// Declared event name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Only declared dimension values are accepted.
        /// </summary>
        public Dictionary<string,string> Dimensions { get; set; } = new Dictionary<string,string>(StringComparer.Ordinal);

    }
}
