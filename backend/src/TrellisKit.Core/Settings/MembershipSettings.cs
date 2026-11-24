namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Membership background workers. The membership product name and price come from the property (membership section).
    /// </summary>
    public class MembershipSettings
    {
        #region Public-Members

        /// <summary>
        /// Minutes between subscription reconciliation passes (re-querying every non-expired store subscription).
        /// Default 1440 (nightly). Minimum 15, maximum 10080.
        /// </summary>
        public int ReconcileIntervalMinutes
        {
            get { return _ReconcileIntervalMinutes; }
            set { _ReconcileIntervalMinutes = Math.Clamp(value, 15, 10080); }
        }

        /// <summary>
        /// Maximum subscriptions re-queried per reconciliation pass. Default 1000. Minimum 1, maximum 100000.
        /// </summary>
        public int ReconcileBatchSize
        {
            get { return _ReconcileBatchSize; }
            set { _ReconcileBatchSize = Math.Clamp(value, 1, 100000); }
        }

        /// <summary>
        /// Minutes between sign-up code expiry reminder passes. Default 60. Minimum 5, maximum 1440.
        /// </summary>
        public int ReminderIntervalMinutes
        {
            get { return _ReminderIntervalMinutes; }
            set { _ReminderIntervalMinutes = Math.Clamp(value, 5, 1440); }
        }

        #endregion

        #region Private-Members

        private int _ReconcileIntervalMinutes = 1440;
        private int _ReconcileBatchSize = 1000;
        private int _ReminderIntervalMinutes = 60;

        #endregion
    }
}
