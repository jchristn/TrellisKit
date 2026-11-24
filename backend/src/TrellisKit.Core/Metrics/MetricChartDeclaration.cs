namespace TrellisKit.Core.Metrics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A generic chart declaration, usable by engine and property pages.
    /// </summary>
    public sealed class MetricChartDeclaration
    {
        /// <summary>Distribution percentile in (0,1]. Default .95.</summary>
        public double Percentile { get; set; } = 0.95;
        /// <summary>Optional threshold reference.</summary>
        public double? Threshold { get; set; } = null;
        /// <summary>
        /// Chart id.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Chart title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Registered metric key.
        /// </summary>
        public string Metric { get; set; } = String.Empty;

        /// <summary>
        /// Line, Area, Bars, RankedBars, Funnel, CohortGrid, HeatMap, Histogram, UptimeStrip, Swimlanes.
        /// </summary>
        public string Form { get; set; } = "Line";

        /// <summary>
        /// Declared metric grouping.
        /// </summary>
        public string? GroupBy { get; set; } = null;

        /// <summary>
        /// Required engine feature, if any.
        /// </summary>
        public string? Feature { get; set; } = null;

    }
}
