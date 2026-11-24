namespace TrellisKit.Core.Exceptions
{
    using System;

    /// <summary>
    /// An external identity token failed verification (bad signature, wrong audience, expired, wrong nonce).
    /// </summary>
    public class IdentityTokenException : Exception
    {
        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">Message suitable for logs. Never include secrets or raw tokens.</param>
        public IdentityTokenException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate the exception with an inner exception.
        /// </summary>
        /// <param name="message">Message suitable for logs. Never include secrets or raw tokens.</param>
        /// <param name="inner">Inner exception.</param>
        public IdentityTokenException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
