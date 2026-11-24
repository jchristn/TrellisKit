namespace TrellisKit.Server.Services
{
    using System;

    /// <summary>
    /// A request body or one of its parts is larger than the configured limit (413 TooLarge).
    /// </summary>
    public class PayloadTooLargeException : Exception
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">Message naming the limit.</param>
        public PayloadTooLargeException(string message) : base(message)
        {
        }

        #endregion
    }
}
