namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Full reply telemetry for administrators (resolved decision 6).
    /// </summary>
    public class MessageTelemetryView
    {
        #region Public-Members

        /// <summary>
        /// Trace id.
        /// </summary>
        public string? TraceId { get; set; } = null;

        /// <summary>
        /// Member message answered.
        /// </summary>
        public string? ReplyToMessageId { get; set; } = null;

        /// <summary>
        /// Retrieval query after rewriting.
        /// </summary>
        public string? RetrievalQuery { get; set; } = null;

        /// <summary>
        /// Time to last token.
        /// </summary>
        public long? TimeToLastTokenMs { get; set; } = null;

        /// <summary>
        /// Tokens per second.
        /// </summary>
        public double? TokensPerSecond { get; set; } = null;

        /// <summary>
        /// Cost in US dollars.
        /// </summary>
        public double CostUsd { get; set; } = 0;

        /// <summary>
        /// Moderation verdict.
        /// </summary>
        public AssistantModerationVerdictEnum? ModerationVerdict { get; set; } = null;

        /// <summary>
        /// Moderation category.
        /// </summary>
        public string? ModerationCategory { get; set; } = null;

        /// <summary>
        /// Per-stage timings and decisions.
        /// </summary>
        public List<MessageStageView> Stages { get; set; } = new List<MessageStageView>();

        /// <summary>
        /// Retrieved knowledge items with scores.
        /// </summary>
        public List<CitationScoreView> RetrievedItems { get; set; } = new List<CitationScoreView>();

        #endregion
    }
}
