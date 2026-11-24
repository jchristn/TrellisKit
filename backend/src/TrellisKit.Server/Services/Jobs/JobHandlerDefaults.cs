namespace TrellisKit.Server.Services.Jobs
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The definition a job handler starts with. Administrators change the schedule, budgets, and concurrency later.
    /// </summary>
    public class JobHandlerDefaults
    {
        #region Public-Members

        /// <summary>
        /// Display name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// What the job does.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Whether the schedule starts enabled. Default false.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Schedule type. Default Manual.
        /// </summary>
        public JobIntervalTypeEnum IntervalType { get; set; } = JobIntervalTypeEnum.Manual;

        /// <summary>
        /// Interval value. Default 1.
        /// </summary>
        public int IntervalValue { get; set; } = 1;

        /// <summary>
        /// Minutes after UTC midnight for daily and weekly schedules. Default 0.
        /// </summary>
        public int TimeOfDayMinutesUtc { get; set; } = 0;

        /// <summary>
        /// Days for DaysOfWeek schedules.
        /// </summary>
        public List<DayOfWeek> DaysOfWeek { get; set; } = new List<DayOfWeek>();

        /// <summary>
        /// Items of one run processed at the same time. Default 2.
        /// </summary>
        public int MaxConcurrency { get; set; } = 2;

        /// <summary>
        /// Attempts per item. Default 3.
        /// </summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>
        /// Cost cap of one run in US dollars, or null for none.
        /// </summary>
        public double? BudgetUsdPerRun { get; set; } = null;

        /// <summary>
        /// Whether items need a CLI agent harness (worker instances only). Default false.
        /// </summary>
        public bool RequiresHarness { get; set; } = false;

        /// <summary>
        /// Whether the handler honors dry runs. Default true.
        /// </summary>
        public bool SupportsDryRun { get; set; } = true;

        #endregion
    }
}
