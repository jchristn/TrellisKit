namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A recurrence rule as stored.
    /// </summary>
    public class EventRecurrenceRuleView
    {
        #region Public-Members

        /// <summary>
        /// Frequency.
        /// </summary>
        public EventRecurrenceFrequencyEnum Frequency { get; set; } = EventRecurrenceFrequencyEnum.Weekly;

        /// <summary>
        /// Interval.
        /// </summary>
        public int Interval { get; set; } = 1;

        /// <summary>
        /// Weekdays (Mon to Sun).
        /// </summary>
        public List<string> Weekdays { get; set; } = new List<string>();

        /// <summary>
        /// Day of the month, or null.
        /// </summary>
        public int? MonthDay { get; set; } = null;

        /// <summary>
        /// Ordinal weekday position, or null.
        /// </summary>
        public int? SetPosition { get; set; } = null;

        /// <summary>
        /// Total occurrences, or null.
        /// </summary>
        public int? Count { get; set; } = null;

        /// <summary>
        /// Last local date, or null.
        /// </summary>
        public string? UntilDate { get; set; } = null;

        #endregion
    }
}
