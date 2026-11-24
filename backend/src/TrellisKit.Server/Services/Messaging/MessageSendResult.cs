namespace TrellisKit.Server.Services.Messaging
{
    using System;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Result of a send: the message payload and whether this call created it (201) or returned an idempotent repeat (200).
    /// </summary>
    public class MessageSendResult
    {
        #region Public-Members

        /// <summary>
        /// The message as the sender sees it.
        /// </summary>
        public DirectMessageView Message { get; set; } = new DirectMessageView();

        /// <summary>
        /// True for 201, false for an idempotent repeat.
        /// </summary>
        public bool Created { get; set; } = false;

        #endregion
    }
}
