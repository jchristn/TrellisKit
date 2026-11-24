namespace TrellisKit.Core.Exceptions
{
    using System;

    /// <summary>
    /// A write violated a unique constraint (for example an email, handle, or sign-up code already in use).
    /// </summary>
    public class DuplicateRecordException : Exception
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">Message naming the record kind. Never include secrets.</param>
        public DuplicateRecordException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate the exception with an inner exception.
        /// </summary>
        /// <param name="message">Message naming the record kind. Never include secrets.</param>
        /// <param name="inner">Provider exception.</param>
        public DuplicateRecordException(string message, Exception inner) : base(message, inner)
        {
        }

        #endregion
    }
}
