namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A validated recurrence rule (the input of the expander).
    /// </summary>
    public class EventRecurrenceRule
    {
        #region Public-Members

        /// <summary>
        /// Frequency.
        /// </summary>
        public EventRecurrenceFrequencyEnum Frequency { get; set; } = EventRecurrenceFrequencyEnum.Weekly;

        /// <summary>
        /// Interval (1 to 99).
        /// </summary>
        public int Interval { get; set; } = 1;

        /// <summary>
        /// Weekdays.
        /// </summary>
        public List<DayOfWeek> Weekdays { get; set; } = new List<DayOfWeek>();

        /// <summary>
        /// Day of the month (1 to 31, or -1 for the last day), or null.
        /// </summary>
        public int? MonthDay { get; set; } = null;

        /// <summary>
        /// Ordinal weekday position (1 to 4, or -1), or null.
        /// </summary>
        public int? SetPosition { get; set; } = null;

        /// <summary>
        /// Total occurrences, or null.
        /// </summary>
        public int? Count { get; set; } = null;

        /// <summary>
        /// Last local date (inclusive; date part only), or null.
        /// </summary>
        public DateTime? UntilDate { get; set; } = null;

        #endregion
    }
}
