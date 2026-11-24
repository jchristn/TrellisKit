namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Bounded category breakdown.
    /// </summary>
    public class EventCategoryMetricsPage
    {
        #region Public-Members

        /// <summary>
        /// At most 20 categories, most events first.
        /// </summary>
        public List<EventCategoryMetricView> Objects { get; set; } = new List<EventCategoryMetricView>();

        /// <summary>
        /// Categories with events.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// Path of the full category list.
        /// </summary>
        public string Enumerate { get; set; } = String.Empty;

        #endregion
    }
}
