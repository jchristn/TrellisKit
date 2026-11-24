namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Poll settings (docs/PHASE3_CONTRACT.md section 15).
    /// </summary>
    public class PollSettings
    {
        #region Public-Members

        /// <summary>
        /// Options a poll may have. Default 20. Minimum 2, maximum 50.
        /// </summary>
        public int MaxOptions
        {
            get { return _MaxOptions; }
            set { _MaxOptions = Math.Clamp(value, 2, 50); }
        }

        /// <summary>
        /// Latest scheduled close, in days ahead. Default 365. Minimum 1, maximum 3650.
        /// </summary>
        public int MaxDurationDays
        {
            get { return _MaxDurationDays; }
            set { _MaxDurationDays = Math.Clamp(value, 1, 3650); }
        }

        /// <summary>
        /// Vote changes per member each minute. Default 60. Minimum 1, maximum 10000.
        /// </summary>
        public int VotesPerMinute
        {
            get { return _VotesPerMinute; }
            set { _VotesPerMinute = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// How often polls past their scheduled close are closed. Default 60. Minimum 10, maximum 3600.
        /// </summary>
        public int CloseIntervalSeconds
        {
            get { return _CloseIntervalSeconds; }
            set { _CloseIntervalSeconds = Math.Clamp(value, 10, 3600); }
        }

        #endregion

        #region Private-Members

        private int _MaxOptions = 20;
        private int _MaxDurationDays = 365;
        private int _VotesPerMinute = 60;
        private int _CloseIntervalSeconds = 60;

        #endregion
    }
}
