namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A conversation with the caller's member row and the other members (one batched load).
    /// </summary>
    public class ConversationEntry
    {
        #region Public-Members

        /// <summary>
        /// Conversation.
        /// </summary>
        public Conversation Conversation { get; set; } = new Conversation();

        /// <summary>
        /// The caller's member row.
        /// </summary>
        public ConversationMember Mine { get; set; } = new ConversationMember();

        /// <summary>
        /// The other members (one for Direct).
        /// </summary>
        public List<ConversationMember> Others
        {
            get { return _Others; }
            set { _Others = value ?? new List<ConversationMember>(); }
        }

        #endregion

        #region Private-Members

        private List<ConversationMember> _Others = new List<ConversationMember>();

        #endregion
    }
}
