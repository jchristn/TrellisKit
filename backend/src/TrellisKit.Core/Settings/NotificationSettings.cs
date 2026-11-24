namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Notification center settings (contract section 8).
    /// </summary>
    public class NotificationSettings
    {
        #region Public-Members

        /// <summary>
        /// Read notifications are deleted after this time; unread ones after twice this. Default 180. Minimum 7, maximum 3650.
        /// </summary>
        public int RetentionDays
        {
            get { return _RetentionDays; }
            set { _RetentionDays = Math.Clamp(value, 7, 3650); }
        }

        /// <summary>
        /// Recipients processed per fan-out batch. Default 500. Minimum 50, maximum 5000.
        /// </summary>
        public int FanoutBatchSize
        {
            get { return _FanoutBatchSize; }
            set { _FanoutBatchSize = Math.Clamp(value, 50, 5000); }
        }

        /// <summary>
        /// Seconds between fan-out worker passes when nothing signals it (new posts signal it immediately). Default 15.
        /// Minimum 1, maximum 3600.
        /// </summary>
        public int FanoutIntervalSeconds
        {
            get { return _FanoutIntervalSeconds; }
            set { _FanoutIntervalSeconds = Math.Clamp(value, 1, 3600); }
        }

        /// <summary>
        /// Start of the push window for new accounts (HH:mm, member local time). Default "07:00". Never null.
        /// </summary>
        public string DefaultHoursStart
        {
            get { return _DefaultHoursStart; }
            set { _DefaultHoursStart = value ?? String.Empty; }
        }

        /// <summary>
        /// End of the push window for new accounts (HH:mm, member local time). Default "22:00". Never null.
        /// </summary>
        public string DefaultHoursEnd
        {
            get { return _DefaultHoursEnd; }
            set { _DefaultHoursEnd = value ?? String.Empty; }
        }

        #endregion

        #region Private-Members

        private int _RetentionDays = 180;
        private int _FanoutBatchSize = 500;
        private int _FanoutIntervalSeconds = 15;
        private string _DefaultHoursStart = "07:00";
        private string _DefaultHoursEnd = "22:00";

        #endregion
    }
}
