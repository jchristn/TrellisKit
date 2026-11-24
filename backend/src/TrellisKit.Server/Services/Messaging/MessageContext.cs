namespace TrellisKit.Server.Services.Messaging
{
    using System;
    using TrellisKit.Core.Models;

    /// <summary>
    /// A message with its conversation from a member's side.
    /// </summary>
    public class MessageContext
    {
        #region Public-Members

        /// <summary>
        /// Message.
        /// </summary>
        public Message Message { get; set; } = new Message();

        /// <summary>
        /// Conversation from the member's side.
        /// </summary>
        public ConversationEntry Entry { get; set; } = new ConversationEntry();

        #endregion
    }
}
