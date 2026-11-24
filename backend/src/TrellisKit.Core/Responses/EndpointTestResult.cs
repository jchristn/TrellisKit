namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Result of an endpoint test or validation (docs/AI_CONTRACT.md section 1.5).
    /// </summary>
    public class EndpointTestResult
    {
        #region Public-Members

        /// <summary>
        /// Whether the call succeeded.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Kind tested.
        /// </summary>
        public ModelEndpointKindEnum Kind { get; set; } = ModelEndpointKindEnum.Completion;

        /// <summary>
        /// Latency.
        /// </summary>
        public long LatencyMs { get; set; } = 0;

        /// <summary>
        /// Time to first token (streaming completions).
        /// </summary>
        public long? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Provider status.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Model.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Output (at most 2000 characters).
        /// </summary>
        public string? Output { get; set; } = null;

        /// <summary>
        /// Reasoning text the model returned separately from the answer (at most 2000 characters), or null.
        /// </summary>
        public string? Reasoning { get; set; } = null;

        /// <summary>
        /// Provider finish reason (for example stop or length), or null when the provider sent none.
        /// </summary>
        public string? FinishReason { get; set; } = null;

        /// <summary>
        /// Completion token cap the test used.
        /// </summary>
        public int? MaxTokens { get; set; } = null;

        /// <summary>
        /// Whether the output stopped at the token cap (finish reason length or max tokens).
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Why a successful completion test may look wrong: ReasoningConsumedBudget (reasoning used the cap before any
        /// answer), EmptyOutput (no text), Truncated (the answer stopped at the cap), or null.
        /// </summary>
        public string? Hint { get; set; } = null;

        /// <summary>
        /// Token usage.
        /// </summary>
        public AiTokenUsageView? Usage { get; set; } = null;

        /// <summary>
        /// Embedding result.
        /// </summary>
        public EmbeddingTestView? Embedding { get; set; } = null;

        /// <summary>
        /// Rerank result.
        /// </summary>
        public RerankTestView? Rerank { get; set; } = null;

        /// <summary>
        /// Sanitized error.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion
    }
}
