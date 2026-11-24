namespace TrellisKit.Core.Metrics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Privacy-safe interval-worker pass record.
    /// </summary>
    public sealed class WorkerPassRecord
    {
        /// <summary>Inline labels (empty for immutable operational records).</summary>
        public List<string> Labels { get; set; } = new List<string>();
        /// <summary>Inline tags (empty for immutable operational records).</summary>
        public Dictionary<string,string> Tags { get; set; } = new Dictionary<string,string>();
        /// <summary>
        /// Run id.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Worker registry name.
        /// </summary>
        public string Worker { get; set; } = String.Empty;

        /// <summary>
        /// Pass start.
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Pass end.
        /// </summary>
        public DateTime EndedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Ok, Failed, or Cancelled.
        /// </summary>
        public string Outcome { get; set; } = "Ok";

        /// <summary>
        /// Items processed.
        /// </summary>
        public int ItemsProcessed { get; set; } = 0;

        /// <summary>
        /// Exception type only; no error messages or content.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Configured interval.
        /// </summary>
        public double IntervalSeconds { get; set; } = 60;

    }
}
