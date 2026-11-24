namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Job engine status of a property: the month's cost against the cap, open work, and the dead-letter count.
    /// </summary>
    public class JobOverviewView
    {
        #region Public-Members

        /// <summary>
        /// Whether the job engine runs on the answering instance (jobs.enabled).
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Whether the answering instance claims harness items (jobs.runHarnesses).
        /// </summary>
        public bool RunsHarnesses { get; set; } = false;

        /// <summary>
        /// Start of the budget month (UTC).
        /// </summary>
        public DateTime MonthStartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Cost of job items this month in US dollars.
        /// </summary>
        public double MonthCostUsd { get; set; } = 0;

        /// <summary>
        /// Monthly cap in US dollars (jobs.monthlyBudgetUsd).
        /// </summary>
        public double MonthlyBudgetUsd { get; set; } = 0;

        /// <summary>
        /// Remaining budget this month in US dollars (never negative).
        /// </summary>
        public double RemainingUsd { get; set; } = 0;

        /// <summary>
        /// Runs queued or running.
        /// </summary>
        public int OpenRuns { get; set; } = 0;

        /// <summary>
        /// Definitions with a registered handler.
        /// </summary>
        public int Definitions { get; set; } = 0;

        /// <summary>
        /// Definitions whose schedule is enabled.
        /// </summary>
        public int EnabledDefinitions { get; set; } = 0;

        #endregion
    }
}
