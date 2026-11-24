namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Event metrics (docs/PHASE4_CONTRACT.md section 4.7).
    /// </summary>
    public class EventMetricsView
    {
        #region Public-Members

        /// <summary>
        /// Window in days.
        /// </summary>
        public int Days { get; set; } = 0;

        /// <summary>
        /// Events starting per day.
        /// </summary>
        public List<EventDayCountView> EventsByDay { get; set; } = new List<EventDayCountView>();

        /// <summary>
        /// RSVPs created per day.
        /// </summary>
        public List<EventDayCountView> RsvpsByDay { get; set; } = new List<EventDayCountView>();

        /// <summary>
        /// Checked-in share of Going RSVPs on past events in the window (0 to 1).
        /// </summary>
        public double CheckInRate { get; set; } = 0;

        /// <summary>
        /// Not checked-in share of Going RSVPs on past events with check-ins (0 to 1).
        /// </summary>
        public double NoShowRate { get; set; } = 0;

        /// <summary>
        /// Categories.
        /// </summary>
        public EventCategoryMetricsPage ByCategory { get; set; } = new EventCategoryMetricsPage();

        #endregion
    }
}
