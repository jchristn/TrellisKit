namespace TrellisKit.Core.Integrations.Implementations
{
    using System;

    /// <summary>
    /// A JWT or JWS failed parsing or validation. Callers translate it into the public exception type for their integration.
    /// </summary>
    internal class JwtValidationException : Exception
    {
        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">Message suitable for logs. Never include secrets or raw tokens.</param>
        internal JwtValidationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate the exception with an inner exception.
        /// </summary>
        /// <param name="message">Message suitable for logs. Never include secrets or raw tokens.</param>
        /// <param name="inner">Inner exception.</param>
        internal JwtValidationException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
