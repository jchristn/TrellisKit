namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Directory counts for the admin dashboard.
    /// </summary>
    public class DirectoryMetricsView
    {
        #region Public-Members

        /// <summary>
        /// Active venues.
        /// </summary>
        public long ActiveVenues { get; set; } = 0;

        /// <summary>
        /// Closed venues.
        /// </summary>
        public long ClosedVenues { get; set; } = 0;

        /// <summary>
        /// Archived venues.
        /// </summary>
        public long ArchivedVenues { get; set; } = 0;

        /// <summary>
        /// Verified active venues.
        /// </summary>
        public long VerifiedVenues { get; set; } = 0;

        /// <summary>
        /// Venues without a position.
        /// </summary>
        public long NeedsPin { get; set; } = 0;

        /// <summary>
        /// Active instructors.
        /// </summary>
        public long Instructors { get; set; } = 0;

        /// <summary>
        /// Instructors linked to an account.
        /// </summary>
        public long ClaimedInstructors { get; set; } = 0;

        /// <summary>
        /// Pending submissions.
        /// </summary>
        public long PendingSubmissions { get; set; } = 0;

        /// <summary>
        /// Pending submissions past the review target.
        /// </summary>
        public long OverdueSubmissions { get; set; } = 0;

        /// <summary>
        /// Submissions created in the last 30 days.
        /// </summary>
        public long SubmissionsLast30Days { get; set; } = 0;

        /// <summary>
        /// Submissions approved in the last 30 days.
        /// </summary>
        public long ApprovedLast30Days { get; set; } = 0;

        /// <summary>
        /// Window of the daily series in days (7 to 365, default 30).
        /// </summary>
        public int Days { get; set; } = 30;

        /// <summary>
        /// Directory submissions created per UTC day, one entry per day of the window (never null).
        /// </summary>
        public List<DailyCount> SubmissionsByDay
        {
            get { return _SubmissionsByDay; }
            set { _SubmissionsByDay = value ?? new List<DailyCount>(); }
        }

        /// <summary>
        /// Directory submissions approved per UTC day (by decision time), one entry per day of the window (never null).
        /// </summary>
        public List<DailyCount> ApprovalsByDay
        {
            get { return _ApprovalsByDay; }
            set { _ApprovalsByDay = value ?? new List<DailyCount>(); }
        }

        /// <summary>
        /// Venues created per UTC day (deleted venues excluded), one entry per day of the window (never null).
        /// </summary>
        public List<DailyCount> VenuesCreatedByDay
        {
            get { return _VenuesCreatedByDay; }
            set { _VenuesCreatedByDay = value ?? new List<DailyCount>(); }
        }

        #endregion

        #region Private-Members

        private List<DailyCount> _SubmissionsByDay = new List<DailyCount>();
        private List<DailyCount> _ApprovalsByDay = new List<DailyCount>();
        private List<DailyCount> _VenuesCreatedByDay = new List<DailyCount>();

        #endregion
    }
}
