namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A conversation between a member or visitor and an assistant.
    /// </summary>
    public class AssistantConversation
    {
        #region Public-Members

        /// <summary>
        /// Owning property id.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Identifier (acv_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Assistant id.
        /// </summary>
        public string AssistantId { get; set; } = String.Empty;

        /// <summary>
        /// Owner member, or null for a visitor.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Owner visitor, or null for a member.
        /// </summary>
        public string? VisitorId { get; set; } = null;

        /// <summary>
        /// Title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Where the title came from.
        /// </summary>
        public AssistantTitleSourceEnum TitleSource { get; set; } = AssistantTitleSourceEnum.Fallback;

        /// <summary>
        /// Status.
        /// </summary>
        public AssistantConversationStatusEnum Status { get; set; } = AssistantConversationStatusEnum.Active;

        /// <summary>
        /// Archived by the owner.
        /// </summary>
        public bool IsArchived { get; set; } = false;

        /// <summary>
        /// Messages of both roles.
        /// </summary>
        public int MessageCount { get; set; } = 0;

        /// <summary>
        /// Last message time.
        /// </summary>
        public DateTime? LastMessageUtc { get; set; } = null;

        /// <summary>
        /// Start of the last message.
        /// </summary>
        public string? LastMessagePreview { get; set; } = null;

        /// <summary>
        /// Surface the conversation started on.
        /// </summary>
        public AiConversationSurfaceEnum Surface { get; set; } = AiConversationSurfaceEnum.Web;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
