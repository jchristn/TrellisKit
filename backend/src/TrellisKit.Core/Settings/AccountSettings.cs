namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Member account settings (asynchronous data export).
    /// </summary>
    public class AccountSettings
    {
        #region Public-Members

        /// <summary>
        /// Hours a finished account export file is kept before the worker deletes it. Default 24, minimum 1, maximum 168.
        /// </summary>
        public int ExportRetentionHours
        {
            get { return _ExportRetentionHours; }
            set { _ExportRetentionHours = Math.Clamp(value, 1, 168); }
        }

        /// <summary>
        /// Account exports a member may request per rolling day. Default 3, minimum 1, maximum 100.
        /// </summary>
        public int ExportsPerDay
        {
            get { return _ExportsPerDay; }
            set { _ExportsPerDay = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Minutes a signed account export download link stays valid. Default 15, minimum 1, maximum 1440.
        /// </summary>
        public int ExportDownloadLinkMinutes
        {
            get { return _ExportDownloadLinkMinutes; }
            set { _ExportDownloadLinkMinutes = Math.Clamp(value, 1, 1440); }
        }

        #endregion

        #region Private-Members

        private int _ExportRetentionHours = 24;
        private int _ExportsPerDay = 3;
        private int _ExportDownloadLinkMinutes = 15;

        #endregion
    }
}
