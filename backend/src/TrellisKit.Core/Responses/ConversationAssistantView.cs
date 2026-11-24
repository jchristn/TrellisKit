namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Assistant reference on a conversation.
    /// </summary>
    public class ConversationAssistantView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Avatar.
        /// </summary>
        public AssistantAvatarView Avatar { get; set; } = new AssistantAvatarView();

        /// <summary>
        /// Whether the assistant can still be used.
        /// </summary>
        public bool Available { get; set; } = true;

        #endregion
    }
}
