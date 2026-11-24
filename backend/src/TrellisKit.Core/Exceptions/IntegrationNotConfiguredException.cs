namespace TrellisKit.Core.Exceptions
{
    using System;

    /// <summary>
    /// An integration was called but its settings are not configured.
    /// </summary>
    public class IntegrationNotConfiguredException : Exception
    {
        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">Message suitable for logs. Never include secrets or raw tokens.</param>
        public IntegrationNotConfiguredException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate the exception with an inner exception.
        /// </summary>
        /// <param name="message">Message suitable for logs. Never include secrets or raw tokens.</param>
        /// <param name="inner">Inner exception.</param>
        public IntegrationNotConfiguredException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
