namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One message of an exported conversation: content, citations, tool calls, and the member's feedback (no model telemetry).
    /// </summary>
    public class AssistantMessageExportItem
    {
        #region Public-Members

        /// <summary>
        /// Message identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// User or Assistant.
        /// </summary>
        public AssistantMessageRoleEnum Role { get; set; } = AssistantMessageRoleEnum.User;

        /// <summary>
        /// Content (Markdown).
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public AssistantMessageStatusEnum Status { get; set; } = AssistantMessageStatusEnum.Completed;

        /// <summary>
        /// Whether a later reply replaced this one.
        /// </summary>
        public bool IsSuperseded { get; set; } = false;

        /// <summary>
        /// Knowledge items cited (bounded by the assistant's knowledge.maxContextItems).
        /// </summary>
        public List<CitationView> Citations { get; set; } = new List<CitationView>();

        /// <summary>
        /// The member's rating of a reply, or null.
        /// </summary>
        public MessageFeedbackView? Feedback { get; set; } = null;

        /// <summary>
        /// Tool calls of a reply (at most 12, the per-turn cap); empty for member messages.
        /// </summary>
        public List<AssistantToolCallExportItem> ToolCalls { get; set; } = new List<AssistantToolCallExportItem>();

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Completed, or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        #endregion
    }
}
