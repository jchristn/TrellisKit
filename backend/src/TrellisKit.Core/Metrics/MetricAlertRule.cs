namespace TrellisKit.Core.Metrics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Metric threshold rule; thresholds are configuration, not property-specific code.
    /// </summary>
    public sealed class MetricAlertRule
    {
        /// <summary>
        /// Stable rule key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Registered metric key.
        /// </summary>
        public string Metric { get; set; } = String.Empty;

        /// <summary>
        /// Threshold value.
        /// </summary>
        public double Threshold { get; set; } = 0;

        /// <summary>
        /// Above or Below.
        /// </summary>
        public string Operator { get; set; } = "Above";

        /// <summary>
        /// Evaluation window, 1 to 10080 minutes.
        /// </summary>
        public int WindowMinutes { get; set; } = 10;

        /// <summary>
        /// Consecutive evaluations required, 1 to 100.
        /// </summary>
        public int Consecutive { get; set; } = 1;

        /// <summary>
        /// Whether this rule runs.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Optional dimension.
        /// </summary>
        public string? GroupBy { get; set; } = null;

    }
}
