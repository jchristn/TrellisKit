namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A recurrence rule (resolved decision 11). Exactly one of count and untilDate, or neither (until the materialization horizon).
    /// </summary>
    public class EventRecurrenceRequest
    {
        #region Public-Members

        /// <summary>
        /// Daily, Weekly, Monthly, or Yearly.
        /// </summary>
        public EventRecurrenceFrequencyEnum? Frequency { get; set; } = null;

        /// <summary>
        /// Interval 1 to 99 (default 1).
        /// </summary>
        public int? Interval { get; set; } = null;

        /// <summary>
        /// Mon to Sun (Weekly: one or more, default the start's weekday; Monthly with setPosition: exactly one).
        /// </summary>
        public List<string>? Weekdays { get; set; } = null;

        /// <summary>
        /// Monthly by date: 1 to 31, or -1 for the last day of the month.
        /// </summary>
        public int? MonthDay { get; set; } = null;

        /// <summary>
        /// Monthly by weekday: 1 to 4, or -1 for the last (with one weekday).
        /// </summary>
        public int? SetPosition { get; set; } = null;

        /// <summary>
        /// Total occurrences including the first (1 to events.maxOccurrences), or null.
        /// </summary>
        public int? Count { get; set; } = null;

        /// <summary>
        /// Last local date (yyyy-MM-dd, inclusive), or null.
        /// </summary>
        public string? UntilDate { get; set; } = null;

        #endregion
    }
}
