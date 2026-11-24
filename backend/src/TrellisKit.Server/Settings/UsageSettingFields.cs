namespace TrellisKit.Server.Settings
{
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The usage metering settings (docs/USAGE_METERING_CONTRACT.md section 10), appended to
    /// <see cref="SettingsRegistry.All"/>. The time zone and week start are per property settings.
    /// </summary>
    public static class UsageSettingFields
    {
        #region Public-Methods

        /// <summary>
        /// Build the usage setting definitions in display order.
        /// </summary>
        /// <returns>Definitions.</returns>
        public static List<SettingFieldDefinition> Build()
        {
            const SettingApplyModeEnum live = SettingApplyModeEnum.Live;
            const string section = "Usage";

            return new List<SettingFieldDefinition>
            {
                SettingFieldDefinition.Boolean("usage.enabled", section, "Usage metering enabled",
                    "Roll up usage per property (hourly and daily rows). Off stops the rollup; recorded request counters still flush.", live, "TK_USAGE_ENABLED",
                    s => s.Usage.Enabled, (s, v) => s.Usage.Enabled = v),
                SettingFieldDefinition.Integer("usage.rollupIntervalMinutes", section, "Rollup interval (minutes)",
                    "Minutes between usage rollup passes of each property.", live, null, 1, 60,
                    s => s.Usage.RollupIntervalMinutes, (s, v) => s.Usage.RollupIntervalMinutes = v),
                SettingFieldDefinition.Integer("usage.closeAfterHours", section, "Close days after (hours)",
                    "Hours after a local day ends before its daily usage rows close and become immutable.", live, null, 1, 168,
                    s => s.Usage.CloseAfterHours, (s, v) => s.Usage.CloseAfterHours = v),
                SettingFieldDefinition.Integer("usage.hourlyRetentionDays", section, "Hourly rows kept (days)",
                    "Days hourly usage rows are kept.", live, null, 7, 400,
                    s => s.Usage.HourlyRetentionDays, (s, v) => s.Usage.HourlyRetentionDays = v),
                SettingFieldDefinition.Integer("usage.dailyRetentionDays", section, "Daily rows kept (days)",
                    "Days daily usage rows are kept; 0 keeps them forever (recommended for billing).", live, null, 0, 36500,
                    s => s.Usage.DailyRetentionDays, (s, v) => s.Usage.DailyRetentionDays = v),
                SettingFieldDefinition.Integer("usage.activityRetentionDays", section, "Member activity kept (days)",
                    "Days member activity hours are kept (distinct active members are exact inside this window).", live, null, 31, 400,
                    s => s.Usage.ActivityRetentionDays, (s, v) => s.Usage.ActivityRetentionDays = v),
                SettingFieldDefinition.Integer("usage.backfillDaysPerPass", section, "Backfill days per pass",
                    "Local days of history computed per pass on the first run.", live, null, 1, 366,
                    s => s.Usage.BackfillDaysPerPass, (s, v) => s.Usage.BackfillDaysPerPass = v),
                SettingFieldDefinition.Integer("usage.storageBatchSize", section, "Storage batch size",
                    "Videos measured and originals reconciled per pass.", live, null, 1, 1000,
                    s => s.Usage.StorageBatchSize, (s, v) => s.Usage.StorageBatchSize = v),
                SettingFieldDefinition.Integer("usage.storageReconcileDays", section, "Storage re-measure (days)",
                    "Days before a video rendition size is measured again.", live, null, 1, 90,
                    s => s.Usage.StorageReconcileDays, (s, v) => s.Usage.StorageReconcileDays = v),
                SettingFieldDefinition.Boolean("usage.recordRequests", section, "Count API requests",
                    "Count API requests and server errors per property (api.requests, api.errors). Applies to new requests at once; restart to change a running counter.", live, "TK_USAGE_RECORD_REQUESTS",
                    s => s.Usage.RecordRequests, (s, v) => s.Usage.RecordRequests = v)
            };
        }

        #endregion
    }
}
