namespace TrellisKit.Server.Services
{
    using Microsoft.Extensions.Logging;
    using System;
    using SyslogLogging;

    /// <summary>
    /// Writes each log line to SyslogLogging (console and file) and, when telemetry is enabled, to the OpenTelemetry
    /// log pipeline (Loki). Lines written inside a request carry the request's trace and span identifiers.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class AppLogger
    {
        #region Public-Members

        /// <summary>
        /// Underlying SyslogLogging module.
        /// </summary>
        public LoggingModule Module { get; }

        #endregion

        #region Private-Members

        private ILogger? _Telemetry;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the logger.
        /// </summary>
        /// <param name="module">SyslogLogging module.</param>
        /// <exception cref="ArgumentNullException">The module is null.</exception>
        public AppLogger(LoggingModule module)
        {
            Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Attach the telemetry logger once the telemetry host has started.
        /// </summary>
        /// <param name="logger">Logger, or null to detach.</param>
        public void AttachTelemetry(ILogger? logger)
        {
            _Telemetry = logger;
        }

        /// <summary>
        /// Write a debug line.
        /// </summary>
        /// <param name="message">Message.</param>
        public void Debug(string message)
        {
            Module.Debug(message);
            Forward(LogLevel.Debug, message, null);
        }

        /// <summary>
        /// Write an informational line.
        /// </summary>
        /// <param name="message">Message.</param>
        public void Info(string message)
        {
            Module.Info(message);
            Forward(LogLevel.Information, message, null);
        }

        /// <summary>
        /// Write a warning line.
        /// </summary>
        /// <param name="message">Message.</param>
        public void Warn(string message)
        {
            Module.Warn(message);
            Forward(LogLevel.Warning, message, null);
        }

        /// <summary>
        /// Write an error line, optionally with an exception.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="exception">Exception, or null.</param>
        public void Error(string message, Exception? exception = null)
        {
            Module.Error(exception == null ? message : message + " " + exception.GetType().Name + ": " + exception.Message);
            Forward(LogLevel.Error, message, exception);
        }

        #endregion

        #region Private-Methods

        private void Forward(LogLevel level, string message, Exception? exception)
        {
            ILogger? logger = _Telemetry;
            if (logger == null) return;
            try
            {
#pragma warning disable CA2254
                logger.Log(level, exception, message);
#pragma warning restore CA2254
            }
            catch (InvalidOperationException)
            {
                // Telemetry is best-effort.
            }
        }

        #endregion
    }
}
