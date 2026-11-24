namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One conversation in the assistantConversations section of the account export (docs/AI_CONTRACT.md section 9.6).
    /// The export writes these fields and then streams the conversation's messages as messages.
    /// </summary>
    public class AssistantConversationExportItem
    {
        #region Public-Members

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Assistant identifier.
        /// </summary>
        public string AssistantId { get; set; } = String.Empty;

        /// <summary>
        /// Assistant title at export time, or null when the assistant was deleted.
        /// </summary>
        public string? AssistantTitle { get; set; } = null;

        /// <summary>
        /// Title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public AssistantConversationStatusEnum Status { get; set; } = AssistantConversationStatusEnum.Active;

        /// <summary>
        /// Whether archived.
        /// </summary>
        public bool IsArchived { get; set; } = false;

        /// <summary>
        /// Where the conversation was held.
        /// </summary>
        public AiConversationSurfaceEnum Surface { get; set; } = AiConversationSurfaceEnum.Web;

        /// <summary>
        /// Messages of both roles.
        /// </summary>
        public int MessageCount { get; set; } = 0;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last message, or null.
        /// </summary>
        public DateTime? LastMessageUtc { get; set; } = null;

        #endregion
    }
}
