namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A scheduled background job of a property (docs/INTEGRATION.md). One row per registered job handler;
    /// administrators change the schedule, budgets, and concurrency.
    /// </summary>
    public class JobDefinition
    {
        #region Public-Members

        /// <summary>
        /// Identifier (jbd_...).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Handler key (for example jobs.prune), unique per property.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Display name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// What the job does.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Whether the schedule fires. Manual runs work while disabled. Default false.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Schedule type. Default Manual.
        /// </summary>
        public JobIntervalTypeEnum IntervalType { get; set; } = JobIntervalTypeEnum.Manual;

        /// <summary>
        /// Interval value for the EveryN types. Default 1, minimum 1, maximum 1440.
        /// </summary>
        public int IntervalValue
        {
            get { return _IntervalValue; }
            set { _IntervalValue = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Minutes after UTC midnight for EveryNDays and DaysOfWeek. Default 0, minimum 0, maximum 1439.
        /// </summary>
        public int TimeOfDayMinutesUtc
        {
            get { return _TimeOfDayMinutesUtc; }
            set { _TimeOfDayMinutesUtc = Math.Clamp(value, 0, 1439); }
        }

        /// <summary>
        /// Days for DaysOfWeek.
        /// </summary>
        public List<DayOfWeek> DaysOfWeek { get; set; } = new List<DayOfWeek>();

        /// <summary>
        /// Items of one run processed at the same time. Default 2, minimum 1, maximum 64.
        /// </summary>
        public int MaxConcurrency
        {
            get { return _MaxConcurrency; }
            set { _MaxConcurrency = Math.Clamp(value, 1, 64); }
        }

        /// <summary>
        /// Attempts per item before it is dead-lettered. Default 3, minimum 1, maximum 10.
        /// </summary>
        public int MaxAttempts
        {
            get { return _MaxAttempts; }
            set { _MaxAttempts = Math.Clamp(value, 1, 10); }
        }

        /// <summary>
        /// Cost cap of one run in micro-dollars, or null for none (the monthly cap across jobs still applies).
        /// </summary>
        public long? BudgetMicrosPerRun { get; set; } = null;

        /// <summary>
        /// Whether items need a CLI agent harness (claimed only by instances with jobs.runHarnesses).
        /// </summary>
        public bool RequiresHarness { get; set; } = false;

        /// <summary>
        /// Next scheduled UTC time, or null when not scheduled.
        /// </summary>
        public DateTime? NextRunUtc { get; set; } = null;

        /// <summary>
        /// Last time a run was queued or skipped by the schedule, or null.
        /// </summary>
        public DateTime? LastRunUtc { get; set; } = null;

        /// <summary>
        /// Lock held while the scheduler fires the definition, or null.
        /// </summary>
        public DateTime? LockedUntilUtc { get; set; } = null;

        /// <summary>
        /// Administrator who last changed the definition, or null.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        /// <summary>
        /// UTC creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time of the last change.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private int _IntervalValue = 1;
        private int _TimeOfDayMinutesUtc = 0;
        private int _MaxConcurrency = 2;
        private int _MaxAttempts = 3;

        #endregion
    }
}
