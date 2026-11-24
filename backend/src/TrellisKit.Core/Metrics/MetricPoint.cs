namespace TrellisKit.Core.Metrics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One chart point with explicit gaps, completeness, and privacy suppression.
    /// </summary>
    public sealed class MetricPoint
    {
        /// <summary>
        /// Inclusive bucket start.
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Exclusive bucket end.
        /// </summary>
        public DateTime EndUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Aggregate, null for a gap or privacy suppression.
        /// </summary>
        public double? Value { get; set; } = null;

        /// <summary>
        /// True when the worker completed this bucket.
        /// </summary>
        public bool Complete { get; set; } = false;

        /// <summary>
        /// True when a small member group was suppressed.
        /// </summary>
        public bool Suppressed { get; set; } = false;

        /// <summary>
        /// Aggregate sample count; zero when suppressed.
        /// </summary>
        public long Samples { get; set; } = 0;

    }
}
