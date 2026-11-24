namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Logging settings.
    /// </summary>
    public class LoggingSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether to log to the console. Default true. Requires restart.
        /// </summary>
        public bool ConsoleLogging { get; set; } = true;

        /// <summary>
        /// Whether to log to a file. Default false. Requires restart.
        /// </summary>
        public bool FileLogging { get; set; } = false;

        /// <summary>
        /// Directory for log files. Default "logs". Requires restart.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null or empty.</exception>
        public string LogDirectory
        {
            get
            {
                return _LogDirectory;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(LogDirectory));
                _LogDirectory = value.Trim();
            }
        }

        /// <summary>
        /// Log filename. Default "trelliskit.log". Requires restart.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null or empty.</exception>
        public string LogFilename
        {
            get
            {
                return _LogFilename;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(LogFilename));
                _LogFilename = value.Trim();
            }
        }

        /// <summary>
        /// Whether to write a debug log line for every completed HTTP request. Default false. Applied live.
        /// </summary>
        public bool LogRequests { get; set; } = false;

        #endregion

        #region Private-Members

        private string _LogDirectory = "logs";
        private string _LogFilename = "trelliskit.log";

        #endregion
    }
}
