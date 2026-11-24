namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using PolyPrompt.Models;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Result of a completion through <see cref="ModelInvoker"/>.
    /// </summary>
    public class CompletionOutcome
    {
        #region Public-Members

        /// <summary>
        /// Whether a reply was produced.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Reply text (partial on a failure after streaming started).
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Reasoning text.
        /// </summary>
        public string? Reasoning { get; set; } = null;

        /// <summary>
        /// Prompt tokens.
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
        /// Finish reason.
        /// </summary>
        public string? FinishReason { get; set; } = null;

        /// <summary>
        /// Time to first token.
        /// </summary>
        public long? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Time to last token.
        /// </summary>
        public long? TimeToLastTokenMs { get; set; } = null;

        /// <summary>
        /// Tokens per second.
        /// </summary>
        public double? TokensPerSecond { get; set; } = null;

        /// <summary>
        /// Total latency including retries.
        /// </summary>
        public long LatencyMs { get; set; } = 0;

        /// <summary>
        /// Endpoint that answered (or was tried last).
        /// </summary>
        public ModelEndpoint? Endpoint { get; set; } = null;

        /// <summary>
        /// Whether the fallback answered.
        /// </summary>
        public bool UsedFallback { get; set; } = false;

        /// <summary>
        /// ModelError, ModelTimeout, ModelUnavailable, ModelBusy, or Cancelled.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Sanitized provider error for logs and the admin test console.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Provider HTTP status.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Cost in millionths of a US dollar.
        /// </summary>
        public long CostMicros { get; set; } = 0;

        /// <summary>
        /// Retries made.
        /// </summary>
        public int Retries { get; set; } = 0;

        /// <summary>
        /// Whether any text reached the caller.
        /// </summary>
        public bool StreamedAny { get; set; } = false;

        /// <summary>
        /// Tool calls the model requested (native or parsed from the prompt protocol); empty for a final answer.
        /// </summary>
        public List<ToolCall> ToolCalls { get; set; } = new List<ToolCall>();

        /// <summary>
        /// The assistant message to append before tool results (carries provider state such as reasoning signatures).
        /// </summary>
        public ChatMessage? AssistantMessage { get; set; } = null;

        /// <summary>
        /// How tools reached the endpoint that answered.
        /// </summary>
        public ModelToolCallingEnum ToolMode { get; set; } = ModelToolCallingEnum.Native;

        /// <summary>
        /// Optional telemetry of the call for the ledger (docs/AI_TELEMETRY.md). Never null. After a tool loop it is the
        /// last round's.
        /// </summary>
        public AiCallDetail Detail { get; set; } = new AiCallDetail();

        /// <summary>
        /// Identifier of the ledger row of the call (aic_...), or null when the row could not be written. After a tool
        /// loop it is the last round's.
        /// </summary>
        public string? CallId { get; set; } = null;

                #endregion
    }
}
