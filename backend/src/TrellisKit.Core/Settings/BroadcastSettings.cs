namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Broadcast settings (docs/PHASE5_CONTRACT.md section 14).
    /// </summary>
    public class BroadcastSettings
    {
        #region Public-Members

        /// <summary>
        /// Broadcasts sent per property per UTC day. Default 3. Minimum 1, maximum 50.
        /// </summary>
        public int MaxPerDay
        {
            get { return _MaxPerDay; }
            set { _MaxPerDay = Math.Clamp(value, 1, 50); }
        }

        /// <summary>
        /// Largest audience a broadcast may target. Default 100000. Minimum 1, maximum 1000000.
        /// </summary>
        public int MaxRecipients
        {
            get { return _MaxRecipients; }
            set { _MaxRecipients = Math.Clamp(value, 1, 1000000); }
        }

        /// <summary>
        /// Recipients expanded per batch by the send worker. Default 500. Minimum 50, maximum 5000.
        /// </summary>
        public int BatchSize
        {
            get { return _BatchSize; }
            set { _BatchSize = Math.Clamp(value, 50, 5000); }
        }

        /// <summary>
        /// Broadcast emails queued per property each minute. Default 600. Minimum 10, maximum 10000.
        /// </summary>
        public int EmailsPerMinute
        {
            get { return _EmailsPerMinute; }
            set { _EmailsPerMinute = Math.Clamp(value, 10, 10000); }
        }

        #endregion

        #region Private-Members

        private int _MaxPerDay = 3;
        private int _MaxRecipients = 100000;
        private int _BatchSize = 500;
        private int _EmailsPerMinute = 600;

        #endregion
    }
}
