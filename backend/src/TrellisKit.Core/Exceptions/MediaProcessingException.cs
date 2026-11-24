namespace TrellisKit.Core.Exceptions
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Media processing failed for a reason that retrying cannot fix (the media becomes Failed with
    /// <see cref="Reason"/>).
    /// </summary>
    public class MediaProcessingException : Exception
    {
        #region Public-Members

        /// <summary>
        /// Failure reason recorded on the media.
        /// </summary>
        public MediaFailureReasonEnum Reason { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="reason">Failure reason.</param>
        /// <param name="message">Message.</param>
        public MediaProcessingException(MediaFailureReasonEnum reason, string message) : base(message)
        {
            Reason = reason;
        }

        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="reason">Failure reason.</param>
        /// <param name="message">Message.</param>
        /// <param name="inner">Inner exception.</param>
        public MediaProcessingException(MediaFailureReasonEnum reason, string message, Exception inner) : base(message, inner)
        {
            Reason = reason;
        }

        #endregion
    }
}
