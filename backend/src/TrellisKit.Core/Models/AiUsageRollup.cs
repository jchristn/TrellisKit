namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A usage rollup row (docs/AI_CONTRACT.md section 14.1).
    /// </summary>
    public class AiUsageRollup
    {
        #region Public-Members

        /// <summary>
        /// Period start.
        /// </summary>
        public DateTime PeriodStartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Hour or Day.
        /// </summary>
        public AiUsageGranularityEnum Granularity { get; set; } = AiUsageGranularityEnum.Hour;

        /// <summary>
        /// Assistant id, or empty.
        /// </summary>
        public string AssistantId { get; set; } = String.Empty;

        /// <summary>
        /// Endpoint id, or empty.
        /// </summary>
        public string EndpointId { get; set; } = String.Empty;

        /// <summary>
        /// Purpose.
        /// </summary>
        public string Purpose { get; set; } = String.Empty;

        /// <summary>
        /// Outcome.
        /// </summary>
        public string Outcome { get; set; } = String.Empty;

        /// <summary>
        /// Test traffic.
        /// </summary>
        public bool IsTest { get; set; } = false;

        /// <summary>
        /// Requests.
        /// </summary>
        public long Requests { get; set; } = 0;

        /// <summary>
        /// Errors.
        /// </summary>
        public long Errors { get; set; } = 0;

        /// <summary>
        /// Prompt tokens.
        /// </summary>
        public long PromptTokens { get; set; } = 0;

        /// <summary>
        /// Completion tokens.
        /// </summary>
        public long CompletionTokens { get; set; } = 0;

        /// <summary>
        /// Reasoning tokens.
        /// </summary>
        public long ReasoningTokens { get; set; } = 0;

        /// <summary>
        /// Cost in millionths of a US dollar.
        /// </summary>
        public long CostMicros { get; set; } = 0;

        /// <summary>
        /// Sum of latencies.
        /// </summary>
        public long LatencyMsSum { get; set; } = 0;

        /// <summary>
        /// Largest latency.
        /// </summary>
        public long LatencyMsMax { get; set; } = 0;

        /// <summary>
        /// Sum of times to first token.
        /// </summary>
        public long TtftMsSum { get; set; } = 0;

        /// <summary>
        /// Calls with a time to first token.
        /// </summary>
        public long TtftCount { get; set; } = 0;

        /// <summary>
        /// Latency histogram counts: 250 ms, 500 ms, 1 s, 2 s, 5 s, 10 s, 30 s, 60 s, above.
        /// </summary>
        public List<long> Buckets { get; set; } = new List<long>();

        #endregion
    }
}
