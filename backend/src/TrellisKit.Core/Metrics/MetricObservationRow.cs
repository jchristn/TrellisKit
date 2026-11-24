namespace TrellisKit.Core.Metrics
{
    using System;
    using System.Collections.Generic;
    /// <summary>One current or comparison bucket, the actual object paged by metric enumeration.</summary>
    public sealed class MetricObservationRow
    {
        /// <summary>Stable identity within the metric selection.</summary>
        public string Id { get; set; } = String.Empty;
        /// <summary>Current or Previous period.</summary>
        public string Period { get; set; } = "Current";
        /// <summary>Group identity, or total.</summary>
        public string SeriesKey { get; set; } = "total";
        /// <summary>Inclusive bucket start.</summary>
        public DateTime StartUtc { get; set; }
        /// <summary>Exclusive bucket end.</summary>
        public DateTime EndUtc { get; set; }
        /// <summary>Measured value, or null for gaps/suppression.</summary>
        public double? Value { get; set; }
        /// <summary>Whether the bucket is complete.</summary>
        public bool Complete { get; set; }
        /// <summary>Small-group suppression.</summary>
        public bool Suppressed { get; set; }
        /// <summary>Observed sample size.</summary>
        public long Samples { get; set; }
        /// <summary>Aggregate rows have no editable member metadata.</summary>
        public List<string> Labels { get; set; } = new List<string>();
        /// <summary>Aggregate rows have no editable member metadata.</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
    }
}
