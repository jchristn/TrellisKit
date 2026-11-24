namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One message of an assistant moderation snapshot (docs/AI_CONTRACT.md section 12.3).
    /// </summary>
    public class AssistantSnapshotMessageView
    {
        #region Public-Members

        /// <summary>
        /// Original message identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Zero-based position in the conversation.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// User or Assistant.
        /// </summary>
        public AssistantMessageRoleEnum Role { get; set; } = AssistantMessageRoleEnum.User;

        /// <summary>
        /// Content (a withheld reply shows the text output moderation replaced).
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Status at the time of the snapshot.
        /// </summary>
        public AssistantMessageStatusEnum Status { get; set; } = AssistantMessageStatusEnum.Completed;

        /// <summary>
        /// Whether this is the reported or withheld message.
        /// </summary>
        public bool IsReported { get; set; } = false;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels (always empty: snapshot copies carry no metadata).
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags (always empty: snapshot copies carry no metadata).
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
