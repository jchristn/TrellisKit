namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One model call in the AI call ledger (docs/INTEGRATION.md): who and what it was for, which executor
    /// answered, tokens, latency, cost, and outcome.
    /// </summary>
    public class AiCallRecord
    {
        #region Public-Members

        /// <summary>
        /// Identifier (aic_...).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// UTC time of the call.
        /// </summary>
        public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Purpose.
        /// </summary>
        public AssistantPurposeEnum Purpose { get; set; } = AssistantPurposeEnum.Chat;

        /// <summary>
        /// Task role, or null for assistant traffic.
        /// </summary>
        public string? Role { get; set; } = null;

        /// <summary>
        /// Assistant, or null.
        /// </summary>
        public string? AssistantId { get; set; } = null;

        /// <summary>
        /// Model endpoint, or null.
        /// </summary>
        public string? EndpointId { get; set; } = null;

        /// <summary>
        /// Executor runtime.
        /// </summary>
        public AiExecutorRuntimeEnum Runtime { get; set; } = AiExecutorRuntimeEnum.ApiEndpoint;

        /// <summary>
        /// Model called.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Position in the role's hierarchy, or null outside a role.
        /// </summary>
        public int? ExecutorPosition { get; set; } = null;

        /// <summary>
        /// Why earlier executors were passed over, or null.
        /// </summary>
        public string? EscalationReason { get; set; } = null;

        /// <summary>
        /// Outcome.
        /// </summary>
        public AiUsageOutcomeEnum Outcome { get; set; } = AiUsageOutcomeEnum.Ok;

        /// <summary>
        /// Error code, or null.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Whether this was the assistant's fallback endpoint.
        /// </summary>
        public bool IsFallback { get; set; } = false;

        /// <summary>
        /// Test traffic (admin consoles).
        /// </summary>
        public bool IsTest { get; set; } = false;

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
        /// Latency in milliseconds.
        /// </summary>
        public long LatencyMs { get; set; } = 0;

        /// <summary>
        /// Cost in micro-dollars.
        /// </summary>
        public long CostMicros { get; set; } = 0;

        /// <summary>
        /// Job key, or null outside a job.
        /// </summary>
        public string? JobKey { get; set; } = null;

        /// <summary>
        /// Job run, or null.
        /// </summary>
        public string? JobRunId { get; set; } = null;

        /// <summary>
        /// Job item, or null.
        /// </summary>
        public string? JobItemId { get; set; } = null;

        /// <summary>
        /// Optional telemetry of the call (docs/AI_TELEMETRY.md). Never null; its values are null where not reported.
        /// </summary>
        public AiCallDetail Detail { get; set; } = new AiCallDetail();

        #endregion
    }
}
