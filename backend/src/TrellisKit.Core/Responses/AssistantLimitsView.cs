namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Assistant limits.
    /// </summary>
    public class AssistantLimitsView
    {
        #region Public-Members

        /// <summary>
        /// Daily member allowance (0 unlimited).
        /// </summary>
        public int MessagesPerMemberPerDay { get; set; } = 50;

        /// <summary>
        /// Daily visitor allowance.
        /// </summary>
        public int MessagesPerVisitorPerDay { get; set; } = 10;

        /// <summary>
        /// Conversation cap.
        /// </summary>
        public int MaxMessagesPerConversation { get; set; } = 100;

        /// <summary>
        /// Reply token cap.
        /// </summary>
        public int MaxTokensPerReply { get; set; } = 800;

        /// <summary>
        /// Longest message.
        /// </summary>
        public int MaxMessageCharacters { get; set; } = 4000;

        /// <summary>
        /// Retention.
        /// </summary>
        public int ConversationRetentionDays { get; set; } = 180;

        #endregion
    }
}
