namespace TrellisKit.Core.Exceptions
{
    using System;

    /// <summary>
    /// A webhook signature is missing, invalid, or outside the allowed time tolerance.
    /// </summary>
    public class WebhookSignatureException : Exception
    {
        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">Message suitable for logs. Never include secrets or raw tokens.</param>
        public WebhookSignatureException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate the exception with an inner exception.
        /// </summary>
        /// <param name="message">Message suitable for logs. Never include secrets or raw tokens.</param>
        /// <param name="inner">Inner exception.</param>
        public WebhookSignatureException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
