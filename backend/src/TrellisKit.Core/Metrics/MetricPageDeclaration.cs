namespace TrellisKit.Core.Metrics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A generic administrator dashboard declaration.
    /// </summary>
    public sealed class MetricPageDeclaration
    {
        /// <summary>Inline labels (empty for immutable operational records).</summary>
        public List<string> Labels { get; set; } = new List<string>();
        /// <summary>Inline tags (empty for immutable operational records).</summary>
        public Dictionary<string,string> Tags { get; set; } = new Dictionary<string,string>();
        /// <summary>
        /// Page id.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Page title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Question this dashboard answers.
        /// </summary>
        public string Question { get; set; } = String.Empty;

        /// <summary>
        /// At most 24 declared charts.
        /// </summary>
        public List<MetricChartDeclaration> Charts { get; set; } = new List<MetricChartDeclaration>();

    }
}
