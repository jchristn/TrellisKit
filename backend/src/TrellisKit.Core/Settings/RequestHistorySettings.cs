namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Request capture and history settings. Applied live.
    /// </summary>
    public class RequestHistorySettings
    {
        #region Public-Members

        /// <summary>
        /// Whether request capture is enabled. Default true. When false, routes still return empty results.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Maximum request body bytes captured before truncation. Default 65536. Minimum 0, maximum 1048576.
        /// </summary>
        public int MaxRequestBodyBytes
        {
            get
            {
                return _MaxRequestBodyBytes;
            }
            set
            {
                _MaxRequestBodyBytes = Math.Clamp(value, 0, 1024 * 1024);
            }
        }

        /// <summary>
        /// Maximum response body bytes captured before truncation. Default 65536. Minimum 0, maximum 1048576.
        /// </summary>
        public int MaxResponseBodyBytes
        {
            get
            {
                return _MaxResponseBodyBytes;
            }
            set
            {
                _MaxResponseBodyBytes = Math.Clamp(value, 0, 1024 * 1024);
            }
        }

        /// <summary>
        /// Days a captured row is retained before pruning. Default 30. Minimum 1, maximum 3650.
        /// </summary>
        public int RetentionDays
        {
            get
            {
                return _RetentionDays;
            }
            set
            {
                _RetentionDays = Math.Clamp(value, 1, 3650);
            }
        }

        /// <summary>
        /// Minutes between pruning passes. Default 60. Minimum 1, maximum 1440.
        /// </summary>
        public int PruneIntervalMinutes
        {
            get
            {
                return _PruneIntervalMinutes;
            }
            set
            {
                _PruneIntervalMinutes = Math.Clamp(value, 1, 1440);
            }
        }

        #endregion

        #region Private-Members

        private int _MaxRequestBodyBytes = 65536;
        private int _MaxResponseBodyBytes = 65536;
        private int _RetentionDays = 30;
        private int _PruneIntervalMinutes = 60;

        #endregion
    }
}
