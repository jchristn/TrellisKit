namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// One line of an administrator summary: a metric value for the summarized day with its comparisons.
    /// </summary>
    public class AdminSummaryRow
    {
        #region Public-Members

        /// <summary>
        /// Metric key.
        /// </summary>
        public string Metric { get; set; } = String.Empty;

        /// <summary>
        /// Display label (the item label, plus the group for grouped items).
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Group key for grouped items (a dimension value or Other), or null.
        /// </summary>
        public string? Group { get; set; } = null;

        /// <summary>
        /// Metric unit (count, bytes, percent, ms, tokens, usdMicros).
        /// </summary>
        public string Unit { get; set; } = "count";

        /// <summary>
        /// Value for the summarized day, or null when there is no data (or it is suppressed).
        /// </summary>
        public double? Value { get; set; } = null;

        /// <summary>
        /// Value for the day before, or null.
        /// </summary>
        public double? PreviousValue { get; set; } = null;

        /// <summary>
        /// Mean of the seven days before the summarized day that have data, or null.
        /// </summary>
        public double? SevenDayAverage { get; set; } = null;

        /// <summary>
        /// Relative change from the previous day (0.25 is 25 percent), or null when either value is missing or zero.
        /// </summary>
        public double? Change { get; set; } = null;

        /// <summary>
        /// Whether the summarized day's bucket was complete when the summary was built.
        /// </summary>
        public bool Complete { get; set; } = false;

        /// <summary>
        /// Whether the value is withheld because the group is smaller than the privacy minimum.
        /// </summary>
        public bool Suppressed { get; set; } = false;

        /// <summary>
        /// Calendar of the bucket: the summary time zone, or UTC for operational metrics.
        /// </summary>
        public string TimeZone { get; set; } = "UTC";

        /// <summary>
        /// Formatted value with comparisons, as shown in the email.
        /// </summary>
        public string Display { get; set; } = String.Empty;

        #endregion
    }
}
