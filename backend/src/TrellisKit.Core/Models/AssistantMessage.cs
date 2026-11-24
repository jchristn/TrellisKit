namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A conversation message with the full reply telemetry (docs/AI_CONTRACT.md section 9.2 and resolved decision 6).
    /// </summary>
    public class AssistantMessage
    {
        #region Public-Members

        /// <summary>
        /// Owning property id.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Identifier (acm_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Conversation id.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Assistant id.
        /// </summary>
        public string AssistantId { get; set; } = String.Empty;

        /// <summary>
        /// User or Assistant.
        /// </summary>
        public AssistantMessageRoleEnum Role { get; set; } = AssistantMessageRoleEnum.User;

        /// <summary>
        /// Markdown content.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Reasoning text (showReasoning only).
        /// </summary>
        public string? Reasoning { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public AssistantMessageStatusEnum Status { get; set; } = AssistantMessageStatusEnum.Completed;

        /// <summary>
        /// Error code of a failed reply.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Endpoint that answered.
        /// </summary>
        public string? EndpointId { get; set; } = null;

        /// <summary>
        /// Endpoint name at answer time.
        /// </summary>
        public string? EndpointName { get; set; } = null;

        /// <summary>
        /// Model that answered.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Whether the fallback endpoint answered.
        /// </summary>
        public bool UsedFallback { get; set; } = false;

        /// <summary>
        /// Prompt tokens (all model calls of the reply).
        /// </summary>
        public int PromptTokens { get; set; } = 0;

        /// <summary>
        /// Completion tokens.
        /// </summary>
        public int CompletionTokens { get; set; } = 0;

        /// <summary>
        /// Reasoning tokens.
        /// </summary>
        public int ReasoningTokens { get; set; } = 0;

        /// <summary>
        /// Total latency.
        /// </summary>
        public long? LatencyMs { get; set; } = null;

        /// <summary>
        /// Time to first token.
        /// </summary>
        public long? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Time to last token.
        /// </summary>
        public long? TimeToLastTokenMs { get; set; } = null;

        /// <summary>
        /// Overall tokens per second.
        /// </summary>
        public double? TokensPerSecond { get; set; } = null;

        /// <summary>
        /// Provider finish reason.
        /// </summary>
        public string? FinishReason { get; set; } = null;

        /// <summary>
        /// Cost in millionths of a US dollar.
        /// </summary>
        public long CostMicros { get; set; } = 0;

        /// <summary>
        /// Moderation verdict.
        /// </summary>
        public AssistantModerationVerdictEnum? ModerationVerdict { get; set; } = null;

        /// <summary>
        /// Moderation category.
        /// </summary>
        public string? ModerationCategory { get; set; } = null;

        /// <summary>
        /// Replaced by a regenerated reply.
        /// </summary>
        public bool IsSuperseded { get; set; } = false;

        /// <summary>
        /// Client idempotency id.
        /// </summary>
        public string? ClientMessageId { get; set; } = null;

        /// <summary>
        /// Trace id of the reply span.
        /// </summary>
        public string? TraceId { get; set; } = null;

        /// <summary>
        /// User message this reply answers.
        /// </summary>
        public string? ReplyToMessageId { get; set; } = null;

        /// <summary>
        /// Query used for knowledge retrieval (after rewriting).
        /// </summary>
        public string? RetrievalQuery { get; set; } = null;

        /// <summary>
        /// Citations, in order.
        /// </summary>
        public List<AssistantCitation> Citations { get; set; } = new List<AssistantCitation>();

        /// <summary>
        /// Per-stage telemetry, in order.
        /// </summary>
        public List<AssistantMessageStage> Stages { get; set; } = new List<AssistantMessageStage>();

        /// <summary>
        /// Tool calls of the reply, in order.
        /// </summary>
        public List<AssistantToolCall> ToolCalls { get; set; } = new List<AssistantToolCall>();

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Completed time.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        #endregion
    }
}
