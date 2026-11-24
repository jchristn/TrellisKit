namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Result of POST /api/v1/conversations.
    /// </summary>
    public class ConversationStartResponse
    {
        #region Public-Members

        /// <summary>
        /// The conversation.
        /// </summary>
        public DirectConversationView Conversation { get; set; } = new DirectConversationView();

        /// <summary>
        /// The first message when one was sent, else null.
        /// </summary>
        public DirectMessageView? Message { get; set; } = null;

        #endregion
    }
}
