namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One model call to add to the usage rollups.
    /// </summary>
    public class AiUsageRecord
    {
        #region Public-Members

        /// <summary>
        /// Assistant, or null.
        /// </summary>
        public string? AssistantId { get; set; } = null;

        /// <summary>
        /// Endpoint, or null.
        /// </summary>
        public string? EndpointId { get; set; } = null;

        /// <summary>
        /// Purpose of the call.
        /// </summary>
        public AssistantPurposeEnum Purpose { get; set; } = AssistantPurposeEnum.Chat;

        /// <summary>
        /// Outcome.
        /// </summary>
        public AiUsageOutcomeEnum Outcome { get; set; } = AiUsageOutcomeEnum.Ok;

        /// <summary>
        /// Admin test traffic.
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
        /// Latency.
        /// </summary>
        public long LatencyMs { get; set; } = 0;

        /// <summary>
        /// Time to first token.
        /// </summary>
        public long? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Cost in millionths of a US dollar.
        /// </summary>
        public long CostMicros { get; set; } = 0;

        /// <summary>
        /// When the call finished.
        /// </summary>
        public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Optional telemetry of the call (docs/AI_TELEMETRY.md). Never null; its values are null where not reported.
        /// </summary>
        public AiCallDetail Detail { get; set; } = new AiCallDetail();

        #endregion
    }
}
