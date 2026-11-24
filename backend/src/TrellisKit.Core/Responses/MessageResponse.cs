namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Simple message body.
    /// </summary>
    public class MessageResponse
    {
        #region Public-Members

        /// <summary>
        /// Message.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty message.
        /// </summary>
        public MessageResponse()
        {
        }

        /// <summary>
        /// Instantiate a message.
        /// </summary>
        /// <param name="message">Message.</param>
        public MessageResponse(string message)
        {
            Message = message ?? String.Empty;
        }

        #endregion
    }
}
