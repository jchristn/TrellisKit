namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Result of a send transaction.
    /// </summary>
    public class MessageSendOutcome
    {
        #region Public-Members

        /// <summary>
        /// The stored message, or null when nothing was stored.
        /// </summary>
        public Message? Message { get; set; } = null;

        /// <summary>
        /// True when this call created the message; false for an idempotent repeat.
        /// </summary>
        public bool Created { get; set; } = false;

        /// <summary>
        /// True when the request message limit refused the send.
        /// </summary>
        public bool LimitReached { get; set; } = false;

        #endregion
    }
}
