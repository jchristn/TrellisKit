namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/metrics/support (docs/PHASE5_CONTRACT.md section 6.2).
    /// </summary>
    public class SupportMetrics
    {
        #region Public-Members

        /// <summary>
        /// Days in the window.
        /// </summary>
        public int Days { get; set; } = 0;

        /// <summary>
        /// Tickets opened per day (one entry per day).
        /// </summary>
        public List<DailyCount> OpenedByDay
        {
            get { return _OpenedByDay; }
            set { _OpenedByDay = value ?? new List<DailyCount>(); }
        }

        /// <summary>
        /// Tickets resolved per day (one entry per day).
        /// </summary>
        public List<DailyCount> ResolvedByDay
        {
            get { return _ResolvedByDay; }
            set { _ResolvedByDay = value ?? new List<DailyCount>(); }
        }

        /// <summary>
        /// Median hours to the first staff reply of tickets opened in the window, or null.
        /// </summary>
        public double? FirstResponseMedianHours { get; set; } = null;

        /// <summary>
        /// Tickets overdue now.
        /// </summary>
        public long OverdueNow { get; set; } = 0;

        /// <summary>
        /// Tickets not Closed now.
        /// </summary>
        public long OpenNow { get; set; } = 0;

        /// <summary>
        /// Tickets per type.
        /// </summary>
        public SupportTypePreview ByType { get; set; } = new SupportTypePreview();

        #endregion

        #region Private-Members

        private List<DailyCount> _OpenedByDay = new List<DailyCount>();
        private List<DailyCount> _ResolvedByDay = new List<DailyCount>();

        #endregion
    }
}
