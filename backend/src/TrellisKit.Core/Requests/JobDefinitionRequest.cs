namespace TrellisKit.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// Change a job definition. Omitted fields keep their values.
    /// </summary>
    public class JobDefinitionRequest
    {
        #region Public-Members

        /// <summary>
        /// Whether the schedule fires, or null.
        /// </summary>
        public bool? Enabled { get; set; } = null;

        /// <summary>
        /// EveryNMinutes, EveryNHours, EveryNDays, DaysOfWeek, or Manual, or null.
        /// </summary>
        public string? IntervalType { get; set; } = null;

        /// <summary>
        /// Interval value (minutes 5 to 1440, hours 1 to 168, days 1 to 90), or null.
        /// </summary>
        public int? IntervalValue { get; set; } = null;

        /// <summary>
        /// Minutes after UTC midnight (0 to 1439) for daily and weekly schedules, or null.
        /// </summary>
        public int? TimeOfDayMinutesUtc { get; set; } = null;

        /// <summary>
        /// Day names (Sunday to Saturday) for DaysOfWeek, or null.
        /// </summary>
        public List<string>? DaysOfWeek { get; set; } = null;

        /// <summary>
        /// Items processed at once (1 to 64), or null.
        /// </summary>
        public int? MaxConcurrency { get; set; } = null;

        /// <summary>
        /// Attempts per item (1 to 10), or null.
        /// </summary>
        public int? MaxAttempts { get; set; } = null;

        /// <summary>
        /// Cost cap per run in US dollars (0 removes the cap; at most 100000), or null.
        /// </summary>
        public double? BudgetUsdPerRun { get; set; } = null;

        #endregion
    }
}
