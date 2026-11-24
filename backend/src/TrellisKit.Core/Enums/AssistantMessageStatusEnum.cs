namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Status of a conversation message.
    /// </summary>
    public enum AssistantMessageStatusEnum
    {
        /// <summary>
        /// Waiting for the model.
        /// </summary>
        Pending,

        /// <summary>
        /// The reply is streaming.
        /// </summary>
        Streaming,

        /// <summary>
        /// Finished.
        /// </summary>
        Completed,

        /// <summary>
        /// The model call failed.
        /// </summary>
        Failed,

        /// <summary>
        /// Cancelled by the member.
        /// </summary>
        Cancelled,

        /// <summary>
        /// Withheld by output moderation.
        /// </summary>
        Blocked
    }
}
