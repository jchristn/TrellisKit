namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One message copied into an <see cref="AiModerationSnapshot"/>.
    /// </summary>
    public class AiModerationSnapshotMessage
    {
        #region Public-Members

        /// <summary>
        /// Snapshot identifier.
        /// </summary>
        public string SnapshotId { get; set; } = String.Empty;

        /// <summary>
        /// Zero-based position in the conversation.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Original message identifier.
        /// </summary>
        public string MessageId { get; set; } = String.Empty;

        /// <summary>
        /// User or Assistant.
        /// </summary>
        public AssistantMessageRoleEnum Role { get; set; } = AssistantMessageRoleEnum.User;

        /// <summary>
        /// Content (for a withheld reply, the text the model produced before output moderation replaced it).
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Message status at the time of the snapshot.
        /// </summary>
        public AssistantMessageStatusEnum Status { get; set; } = AssistantMessageStatusEnum.Completed;

        /// <summary>
        /// When the message was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
