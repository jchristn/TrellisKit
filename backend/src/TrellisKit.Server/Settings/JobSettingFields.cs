namespace TrellisKit.Server.Settings
{
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Settings registry fields of the job engine (docs/INTEGRATION.md).
    /// </summary>
    public static class JobSettingFields
    {
        #region Public-Methods

        /// <summary>
        /// Build the fields.
        /// </summary>
        /// <returns>Fields.</returns>
        public static List<SettingFieldDefinition> Build()
        {
            const SettingApplyModeEnum live = SettingApplyModeEnum.Live;
            const string section = "Jobs";

            return new List<SettingFieldDefinition>
            {
                SettingFieldDefinition.Boolean("jobs.enabled", section, "Job engine enabled",
                    "Schedule and process background jobs on this instance. Off stops scheduling and processing; history stays readable.", live, "TK_JOBS_ENABLED",
                    s => s.Jobs.Enabled, (s, v) => s.Jobs.Enabled = v),
                SettingFieldDefinition.Integer("jobs.tickSeconds", section, "Pass interval (seconds)",
                    "Seconds between scheduler and item processing passes.", live, null, 5, 600,
                    s => s.Jobs.TickSeconds, (s, v) => s.Jobs.TickSeconds = v),
                SettingFieldDefinition.Decimal("jobs.monthlyBudgetUsd", section, "Monthly job budget (USD)",
                    "Cost cap across every job of a property per UTC calendar month. Runs are skipped and running runs stop claiming items once it is reached.", live, "TK_JOBS_MONTHLY_BUDGET_USD", 0, 1000000,
                    s => s.Jobs.MonthlyBudgetUsd, (s, v) => s.Jobs.MonthlyBudgetUsd = v),
                SettingFieldDefinition.Integer("jobs.maxRunsPerPass", section, "Runs planned per pass",
                    "Queued runs a processing pass plans.", live, null, 1, 100,
                    s => s.Jobs.MaxRunsPerPass, (s, v) => s.Jobs.MaxRunsPerPass = v),
                SettingFieldDefinition.Integer("jobs.itemLockMinutes", section, "Item claim (minutes)",
                    "Minutes an item claim lasts before another worker may retry it.", live, null, 1, 1440,
                    s => s.Jobs.ItemLockMinutes, (s, v) => s.Jobs.ItemLockMinutes = v),
                SettingFieldDefinition.Integer("jobs.retryBaseSeconds", section, "Retry base delay (seconds)",
                    "First retry delay, doubled per attempt with jitter.", live, null, 1, 3600,
                    s => s.Jobs.RetryBaseSeconds, (s, v) => s.Jobs.RetryBaseSeconds = v),
                SettingFieldDefinition.Integer("jobs.retryMaxSeconds", section, "Retry longest delay (seconds)",
                    "Longest retry delay.", live, null, 1, 86400,
                    s => s.Jobs.RetryMaxSeconds, (s, v) => s.Jobs.RetryMaxSeconds = v),
                SettingFieldDefinition.Boolean("jobs.runHarnesses", section, "Run CLI agent harnesses",
                    "Claim job items that need a CLI agent harness. Turn on only for worker instances whose image carries the CLIs; harnesses never run in request paths.", live, "TK_JOBS_RUN_HARNESSES",
                    s => s.Jobs.RunHarnesses, (s, v) => s.Jobs.RunHarnesses = v),
                SettingFieldDefinition.Integer("jobs.harnessTimeoutSeconds", section, "Harness call timeout (seconds)",
                    "Longest one CLI agent harness call may run before its process tree is killed. Keep it below the item claim.", live, "TK_JOBS_HARNESS_TIMEOUT_SECONDS", 30, 3600,
                    s => s.Jobs.HarnessTimeoutSeconds, (s, v) => s.Jobs.HarnessTimeoutSeconds = v),
                SettingFieldDefinition.Integer("jobs.harnessMaxToolCalls", section, "Job tool calls per agent call",
                    "Most job tool calls one agent call (harness or API tool loop) may make; later calls are refused.", live, null, 1, 500,
                    s => s.Jobs.HarnessMaxToolCalls, (s, v) => s.Jobs.HarnessMaxToolCalls = v),
                SettingFieldDefinition.Text("jobs.harnessPassEnvironment", section, "Harness environment allowlist",
                    "Extra environment variable names (comma separated) passed to harness processes. Every other server variable, including every TK_ setting, is removed.", live, "TK_JOBS_HARNESS_PASS_ENVIRONMENT",
                    s => s.Jobs.HarnessPassEnvironment, (s, v) => s.Jobs.HarnessPassEnvironment = v),
                SettingFieldDefinition.Integer("jobs.retentionDays", section, "History kept (days)",
                    "Days finished runs and their items are kept.", live, null, 7, 3650,
                    s => s.Jobs.RetentionDays, (s, v) => s.Jobs.RetentionDays = v)
            };
        }

        #endregion
    }
}
