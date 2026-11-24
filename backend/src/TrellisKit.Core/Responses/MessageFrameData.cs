namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Data of the message.created and message.updated frames (docs/PHASE5_CONTRACT.md section 4.5).
    /// </summary>
    public class MessageFrameData
    {
        #region Public-Members

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// The message as the recipient sees it.
        /// </summary>
        public DirectMessageView Message { get; set; } = new DirectMessageView();

        #endregion
    }
}
