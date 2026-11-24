namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Limits of an assistant for the caller.
    /// </summary>
    public class AssistantMemberLimitsView
    {
        #region Public-Members

        /// <summary>
        /// Longest message.
        /// </summary>
        public int MaxMessageCharacters { get; set; } = 4000;

        /// <summary>
        /// Conversation cap.
        /// </summary>
        public int MaxMessagesPerConversation { get; set; } = 100;

        /// <summary>
        /// Daily allowance (null when unlimited).
        /// </summary>
        public int? MessagesPerDay { get; set; } = null;

        /// <summary>
        /// Messages left today (null when unlimited).
        /// </summary>
        public int? RemainingToday { get; set; } = null;

        #endregion
    }
}
