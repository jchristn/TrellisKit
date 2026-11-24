namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A conversation (docs/AI_CONTRACT.md section 9.1).
    /// </summary>
    public class ConversationView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// The assistant.
        /// </summary>
        public ConversationAssistantView Assistant { get; set; } = new ConversationAssistantView();

        /// <summary>
        /// Title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Messages (both roles).
        /// </summary>
        public int MessageCount { get; set; } = 0;

        /// <summary>
        /// Last message.
        /// </summary>
        public DateTime? LastMessageUtc { get; set; } = null;

        /// <summary>
        /// Start of the last message.
        /// </summary>
        public string? LastMessagePreview { get; set; } = null;

        /// <summary>
        /// Archived.
        /// </summary>
        public bool IsArchived { get; set; } = false;

        /// <summary>
        /// Status.
        /// </summary>
        public AssistantConversationStatusEnum Status { get; set; } = AssistantConversationStatusEnum.Active;

        /// <summary>
        /// Surface.
        /// </summary>
        public AiConversationSurfaceEnum Surface { get; set; } = AiConversationSurfaceEnum.Web;

        /// <summary>
        /// Owner (administrator views only).
        /// </summary>
        public ConversationOwnerView? Owner { get; set; } = null;

        /// <summary>
        /// Recent messages (single reads only).
        /// </summary>
        public MessagePageView? RecentMessages { get; set; } = null;

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
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
