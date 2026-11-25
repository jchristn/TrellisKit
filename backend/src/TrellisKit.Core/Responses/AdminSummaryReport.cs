namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The administrator summary of one local day (GET /api/v1/admin/summary, and the staff_admin_summary email).
    /// Bounded: at most the default items plus 60 configured items, each with at most 21 group lines.
    /// </summary>
    public class AdminSummaryReport
    {
        #region Public-Members

        /// <summary>
        /// Property id.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Property name.
        /// </summary>
        public string PropertyName { get; set; } = String.Empty;

        /// <summary>
        /// Summarized local date (yyyy-MM-dd).
        /// </summary>
        public string Date { get; set; } = String.Empty;

        /// <summary>
        /// Time zone that defines the date.
        /// </summary>
        public string TimeZone { get; set; } = "UTC";

        /// <summary>
        /// Start of the summarized day (UTC, inclusive).
        /// </summary>
        public DateTime FromUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// End of the summarized day (UTC, exclusive).
        /// </summary>
        public DateTime ToUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// When the report was built (UTC).
        /// </summary>
        public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Sections in display order (empty sections are left out).
        /// </summary>
        public List<AdminSummarySection> Sections { get; set; } = new List<AdminSummarySection>();

        /// <summary>
        /// Configured items that were skipped (unknown metric, unsupported grouping, or a disabled feature), with the reason.
        /// </summary>
        public List<string> Skipped { get; set; } = new List<string>();

        #endregion
    }
}
