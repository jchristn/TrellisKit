namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Status of a conversation.
    /// </summary>
    public enum AssistantConversationStatusEnum
    {
        /// <summary>
        /// Open for messages.
        /// </summary>
        Active,

        /// <summary>
        /// Reached the message cap.
        /// </summary>
        Full,

        /// <summary>
        /// The assistant is disabled or deleted.
        /// </summary>
        AssistantUnavailable
    }
}
