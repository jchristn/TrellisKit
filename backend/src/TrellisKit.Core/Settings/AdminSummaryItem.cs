namespace TrellisKit.Core.Settings
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One line (or one grouped breakdown) of the daily administrator summary email (docs/ADMIN_SUMMARY.md). An item names
    /// a registered metric key (an engine metric, a usage meter, or a metric the property declares in property.json
    /// metrics.definitions); it never contains a query. Items whose metric is not registered for a property are skipped.
    /// </summary>
    public class AdminSummaryItem
    {
        #region Public-Members

        /// <summary>
        /// Section heading the item appears under (1 to 60 characters). Items with the same section are grouped in the
        /// order the section first appears. Default "Additional metrics".
        /// </summary>
        public string Section
        {
            get { return _Section; }
            set { _Section = String.IsNullOrWhiteSpace(value) ? "Additional metrics" : value.Trim(); }
        }

        /// <summary>
        /// Registered metric key, for example users.new.
        /// </summary>
        public string Metric { get; set; } = String.Empty;

        /// <summary>
        /// Display label (up to 80 characters), or null for the metric's registered title.
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// Dimension to break the value down by (one of the metric's declared groupings), or null for a single total.
        /// </summary>
        public string? GroupBy { get; set; } = null;

        /// <summary>
        /// Exact dimension filters (at most eight, each a declared grouping).
        /// </summary>
        public Dictionary<string, string> Filters
        {
            get { return _Filters; }
            set { _Filters = value ?? new Dictionary<string, string>(StringComparer.Ordinal); }
        }

        /// <summary>
        /// Percentile for distribution metrics, greater than 0 and at most 1. Default 0.95.
        /// </summary>
        public double Percentile
        {
            get { return _Percentile; }
            set { _Percentile = Double.IsFinite(value) && value > 0 && value <= 1 ? value : 0.95; }
        }

        /// <summary>
        /// Leave the item out when the summarized day and the seven days before it have no observations. Default false
        /// (the line shows "No data").
        /// </summary>
        public bool HideWhenEmpty { get; set; } = false;

        /// <summary>
        /// Property feature the item depends on (for example forums, events, library, directory, messaging, support,
        /// ideas, broadcasts, ai), or null. The item is skipped when the property turns that feature off.
        /// </summary>
        public string? Feature { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Section = "Additional metrics";
        private Dictionary<string, string> _Filters = new Dictionary<string, string>(StringComparer.Ordinal);
        private double _Percentile = 0.95;

        #endregion
    }
}
