namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Usage metering settings (docs/USAGE_METERING_CONTRACT.md section 10). Deployment-level; the time zone and week
    /// start are per property settings.
    /// </summary>
    public class UsageSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether the usage worker rolls up usage. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Minutes between rollup passes. Default 5. Minimum 1, maximum 60.
        /// </summary>
        public int RollupIntervalMinutes
        {
            get { return _RollupIntervalMinutes; }
            set { _RollupIntervalMinutes = Math.Clamp(value, 1, 60); }
        }

        /// <summary>
        /// Hours after a local day ends before its daily rows close (become immutable). Default 48. Minimum 1, maximum 168.
        /// </summary>
        public int CloseAfterHours
        {
            get { return _CloseAfterHours; }
            set { _CloseAfterHours = Math.Clamp(value, 1, 168); }
        }

        /// <summary>
        /// Days hourly rows are kept. Default 90. Minimum 7, maximum 400.
        /// </summary>
        public int HourlyRetentionDays
        {
            get { return _HourlyRetentionDays; }
            set { _HourlyRetentionDays = Math.Clamp(value, 7, 400); }
        }

        /// <summary>
        /// Days daily rows are kept; 0 keeps them forever. Default 0. Minimum 0, maximum 36500.
        /// </summary>
        public int DailyRetentionDays
        {
            get { return _DailyRetentionDays; }
            set { _DailyRetentionDays = Math.Clamp(value, 0, 36500); }
        }

        /// <summary>
        /// Days member activity hours are kept. Default 35. Minimum 31, maximum 400.
        /// </summary>
        public int ActivityRetentionDays
        {
            get { return _ActivityRetentionDays; }
            set { _ActivityRetentionDays = Math.Clamp(value, 31, 400); }
        }

        /// <summary>
        /// Local days backfilled per pass on the first run. Default 92. Minimum 1, maximum 366.
        /// </summary>
        public int BackfillDaysPerPass
        {
            get { return _BackfillDaysPerPass; }
            set { _BackfillDaysPerPass = Math.Clamp(value, 1, 366); }
        }

        /// <summary>
        /// Media measured or reconciled per pass. Default 100. Minimum 1, maximum 1000.
        /// </summary>
        public int StorageBatchSize
        {
            get { return _StorageBatchSize; }
            set { _StorageBatchSize = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Days before a video rendition measurement is refreshed. Default 7. Minimum 1, maximum 90.
        /// </summary>
        public int StorageReconcileDays
        {
            get { return _StorageReconcileDays; }
            set { _StorageReconcileDays = Math.Clamp(value, 1, 90); }
        }

        /// <summary>
        /// Whether API requests are counted (api.requests, api.errors). Default true.
        /// </summary>
        public bool RecordRequests { get; set; } = true;

        #endregion

        #region Private-Members

        private int _RollupIntervalMinutes = 5;
        private int _CloseAfterHours = 48;
        private int _HourlyRetentionDays = 90;
        private int _DailyRetentionDays = 0;
        private int _ActivityRetentionDays = 35;
        private int _BackfillDaysPerPass = 92;
        private int _StorageBatchSize = 100;
        private int _StorageReconcileDays = 7;

        #endregion
    }
}
