namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;

    /// <summary>
    /// One row of the AI call ledger. The optional telemetry of <see cref="AiCallDetail"/> (timings, cached tokens,
    /// tool use, transport, provider identifiers) is part of the row; each value is null when not reported
    /// (docs/AI_TELEMETRY.md).
    /// </summary>
    public class AiCallView : AiCallDetail, IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// UTC time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Purpose.
        /// </summary>
        public string Purpose { get; set; } = "Chat";

        /// <summary>
        /// Task role, or null.
        /// </summary>
        public string? Role { get; set; } = null;

        /// <summary>
        /// Assistant, or null.
        /// </summary>
        public string? AssistantId { get; set; } = null;

        /// <summary>
        /// Endpoint, or null.
        /// </summary>
        public string? EndpointId { get; set; } = null;

        /// <summary>
        /// Runtime.
        /// </summary>
        public string Runtime { get; set; } = "ApiEndpoint";

        /// <summary>
        /// Model, or null.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Hierarchy position, or null.
        /// </summary>
        public int? ExecutorPosition { get; set; } = null;

        /// <summary>
        /// Why earlier executors were passed over, or null.
        /// </summary>
        public string? EscalationReason { get; set; } = null;

        /// <summary>
        /// Outcome.
        /// </summary>
        public string Outcome { get; set; } = "Ok";

        /// <summary>
        /// Error code, or null.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Assistant fallback endpoint.
        /// </summary>
        public bool IsFallback { get; set; } = false;

        /// <summary>
        /// Test traffic.
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
        /// Input tokens: the prompt tokens under the provider-neutral name.
        /// </summary>
        public int InputTokens
        {
            get { return PromptTokens; }
        }

        /// <summary>
        /// Output tokens: the completion tokens under the provider-neutral name.
        /// </summary>
        public int OutputTokens
        {
            get { return CompletionTokens; }
        }

        /// <summary>
        /// Latency in milliseconds.
        /// </summary>
        public long LatencyMs { get; set; } = 0;

        /// <summary>
        /// Cost in US dollars.
        /// </summary>
        public double CostUsd { get; set; } = 0;

        /// <summary>
        /// Job key, or null.
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

        /// <inheritdoc />
        public List<string> Labels { get; set; } = new List<string>();

        /// <inheritdoc />
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
