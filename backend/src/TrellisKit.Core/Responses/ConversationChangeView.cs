namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One entry of the conversation sync (docs/PHASE5_CONTRACT.md section 5.2).
    /// </summary>
    public class ConversationChangeView
    {
        #region Public-Members

        /// <summary>
        /// The conversation as the caller sees it now.
        /// </summary>
        public DirectConversationView Conversation { get; set; } = new DirectConversationView();

        /// <summary>
        /// Updated or Removed.
        /// </summary>
        public ConversationChangeEnum Change { get; set; } = ConversationChangeEnum.Updated;

        #endregion
    }
}
