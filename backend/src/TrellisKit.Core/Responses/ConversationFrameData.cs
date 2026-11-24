namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Data of the conversation.created and conversation.updated frames.
    /// </summary>
    public class ConversationFrameData
    {
        #region Public-Members

        /// <summary>
        /// The conversation as the recipient sees it.
        /// </summary>
        public DirectConversationView Conversation { get; set; } = new DirectConversationView();

        #endregion
    }
}
