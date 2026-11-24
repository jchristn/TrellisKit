namespace Test.Shared
{
    using System;
    using SyslogLogging;

    /// <summary>
    /// Creates silent logging modules by default; TK_TEST_LOG_CONSOLE=true enables diagnostic server output.
    /// </summary>
    public static class TestLogging
    {
        #region Public-Methods

        /// <summary>
        /// Create a logging module with console output disabled.
        /// </summary>
        /// <returns>Module.</returns>
        public static LoggingModule CreateModule()
        {
            LoggingModule module = new LoggingModule();
            module.Settings.EnableConsole = String.Equals(Environment.GetEnvironmentVariable("TK_TEST_LOG_CONSOLE"), "true", StringComparison.OrdinalIgnoreCase);
            return module;
        }

        #endregion
    }
}
