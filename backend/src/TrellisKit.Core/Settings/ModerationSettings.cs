namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Moderation settings (contract section 9).
    /// </summary>
    public class ModerationSettings
    {
        #region Public-Members

        /// <summary>
        /// Distinct reporters with open reports that hide content pending review. 0 turns auto-hide off. Never applies to SystemAdmin content. Default 3. Minimum 0, maximum 100.
        /// </summary>
        public int AutoHideReportThreshold
        {
            get { return _AutoHideReportThreshold; }
            set { _AutoHideReportThreshold = Math.Clamp(value, 0, 100); }
        }

        /// <summary>
        /// Removed and deleted content (and its media) is purged after this time. Default 365. Minimum 30, maximum 3650.
        /// </summary>
        public int RetainRemovedDays
        {
            get { return _RetainRemovedDays; }
            set { _RetainRemovedDays = Math.Clamp(value, 30, 3650); }
        }

        /// <summary>
        /// Reports older than this are overdue and notify moderators. Default 12. Minimum 1, maximum 168.
        /// </summary>
        public int ReviewTargetHours
        {
            get { return _ReviewTargetHours; }
            set { _ReviewTargetHours = Math.Clamp(value, 1, 168); }
        }

        /// <summary>
        /// Screen member text with the word filter list. Default true.
        /// </summary>
        public bool WordFilterEnabled { get; set; } = true;

        /// <summary>
        /// Reports a member may file each hour. Default 20. Minimum 1, maximum 1000.
        /// </summary>
        public int ReportsPerHour
        {
            get { return _ReportsPerHour; }
            set { _ReportsPerHour = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Blocks and unblocks a member may make each hour. Default 30. Minimum 1, maximum 1000.
        /// </summary>
        public int BlocksPerHour
        {
            get { return _BlocksPerHour; }
            set { _BlocksPerHour = Math.Clamp(value, 1, 1000); }
        }

        #endregion

        #region Private-Members

        private int _AutoHideReportThreshold = 3;
        private int _RetainRemovedDays = 365;
        private int _ReviewTargetHours = 12;
        private int _ReportsPerHour = 20;
        private int _BlocksPerHour = 30;

        #endregion
    }
}
