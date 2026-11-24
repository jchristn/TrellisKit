namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A recurrence rule shared by the occurrences of a repeating event (resolved decision 11).
    /// </summary>
    public class EventRecurrence
    {
        #region Public-Members

        /// <summary>
        /// Identifier (erc_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Frequency.
        /// </summary>
        public EventRecurrenceFrequencyEnum Frequency { get; set; } = EventRecurrenceFrequencyEnum.Weekly;

        /// <summary>
        /// Interval (1 to 99).
        /// </summary>
        public int Interval { get; set; } = 1;

        /// <summary>
        /// Total occurrences including the first, or null.
        /// </summary>
        public int? Count { get; set; } = null;

        /// <summary>
        /// Last local date (yyyy-MM-dd, inclusive), or null.
        /// </summary>
        public string? UntilLocalDate { get; set; } = null;

        /// <summary>
        /// Day of the month (1 to 31, or -1 for the last day), or null.
        /// </summary>
        public int? MonthDay { get; set; } = null;

        /// <summary>
        /// Ordinal of the weekday in the month (1 to 4, or -1 for the last), or null.
        /// </summary>
        public int? SetPosition { get; set; } = null;

        /// <summary>
        /// Weekdays (Weekly; one weekday with SetPosition for Monthly).
        /// </summary>
        public List<DayOfWeek> Weekdays { get; set; } = new List<DayOfWeek>();

        /// <summary>
        /// IANA time zone.
        /// </summary>
        public string TimeZone { get; set; } = "UTC";

        /// <summary>
        /// Local start of the first occurrence (yyyy-MM-ddTHH:mm).
        /// </summary>
        public string StartLocal { get; set; } = String.Empty;

        /// <summary>
        /// Duration of each occurrence in minutes.
        /// </summary>
        public int DurationMinutes { get; set; } = 0;

        /// <summary>
        /// Whether occurrences are all-day.
        /// </summary>
        public bool IsAllDay { get; set; } = false;

        /// <summary>
        /// Occurrences starting up to this time are materialized, or null.
        /// </summary>
        public DateTime? MaterializedThroughUtc { get; set; } = null;

        /// <summary>
        /// Occurrences materialized so far.
        /// </summary>
        public int MaterializedCount { get; set; } = 0;

        /// <summary>
        /// ICS SEQUENCE of the series master.
        /// </summary>
        public int IcsSequence { get; set; } = 0;

        /// <summary>
        /// The series template event row, or null.
        /// </summary>
        public string? TemplateEventId { get; set; } = null;

        /// <summary>
        /// Creator, or null.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// When deleted, or null.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
