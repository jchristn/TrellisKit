namespace TrellisKit.Core.Metrics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An operational annotation owned by a property.
    /// </summary>
    public sealed class MetricAnnotation
    {
        /// <summary>Inline labels (empty for immutable operational records).</summary>
        public List<string> Labels { get; set; } = new List<string>();
        /// <summary>Inline tags (empty for immutable operational records).</summary>
        public Dictionary<string,string> Tags { get; set; } = new Dictionary<string,string>();
        /// <summary>
        /// Stable idempotency key.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Declared operational event kind.
        /// </summary>
        public string Kind { get; set; } = "Deploy";

        /// <summary>
        /// Short public administrative description, never content.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// UTC event time.
        /// </summary>
        public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;

    }
}
