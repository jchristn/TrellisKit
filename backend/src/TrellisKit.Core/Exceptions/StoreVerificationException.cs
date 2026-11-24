namespace TrellisKit.Core.Exceptions
{
    using System;

    /// <summary>
    /// An app store transaction, notification, or API response could not be verified.
    /// </summary>
    public class StoreVerificationException : Exception
    {
        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">Message suitable for logs. Never include secrets or raw tokens.</param>
        public StoreVerificationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate the exception with an inner exception.
        /// </summary>
        /// <param name="message">Message suitable for logs. Never include secrets or raw tokens.</param>
        /// <param name="inner">Inner exception.</param>
        public StoreVerificationException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
