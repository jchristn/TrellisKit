namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A conversation message (docs/AI_CONTRACT.md section 9.2).
    /// </summary>
    public class MessageView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Conversation id.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// User or Assistant.
        /// </summary>
        public AssistantMessageRoleEnum Role { get; set; } = AssistantMessageRoleEnum.User;

        /// <summary>
        /// Markdown content.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public AssistantMessageStatusEnum Status { get; set; } = AssistantMessageStatusEnum.Completed;

        /// <summary>
        /// Always true for replies.
        /// </summary>
        public bool IsAiGenerated { get; set; } = false;

        /// <summary>
        /// Reasoning (showReasoning only).
        /// </summary>
        public string? Reasoning { get; set; } = null;

        /// <summary>
        /// Citations.
        /// </summary>
        public List<CitationView> Citations { get; set; } = new List<CitationView>();

        /// <summary>
        /// Tool calls of a reply (bounded preview).
        /// </summary>
        public ToolCallPageView ToolCalls { get; set; } = new ToolCallPageView();

        /// <summary>
        /// Feedback on a reply.
        /// </summary>
        public MessageFeedbackView? Feedback { get; set; } = null;

        /// <summary>
        /// Model details (administrators, or members when ai.showModelDetailsToMembers).
        /// </summary>
        public MessageModelView? Model { get; set; } = null;

        /// <summary>
        /// Token usage (same visibility as model).
        /// </summary>
        public AiTokenUsageView? Usage { get; set; } = null;

        /// <summary>
        /// Latency (same visibility).
        /// </summary>
        public long? LatencyMs { get; set; } = null;

        /// <summary>
        /// Time to first token (same visibility).
        /// </summary>
        public long? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Finish reason.
        /// </summary>
        public string? FinishReason { get; set; } = null;

        /// <summary>
        /// Error code of a failed reply.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Replaced by a regenerated reply.
        /// </summary>
        public bool IsSuperseded { get; set; } = false;

        /// <summary>
        /// Client idempotency id.
        /// </summary>
        public string? ClientMessageId { get; set; } = null;

        /// <summary>
        /// Full reply telemetry (administrator views only).
        /// </summary>
        public MessageTelemetryView? Telemetry { get; set; } = null;

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
        /// Completed.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        #endregion
    }
}
