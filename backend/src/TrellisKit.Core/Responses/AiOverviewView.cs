namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// GET /api/v1/admin/ai/analytics/overview.
    /// </summary>
    public class AiOverviewView
    {
        #region Public-Members

        /// <summary>
        /// Window.
        /// </summary>
        public int Days { get; set; } = 0;

        /// <summary>
        /// Conversations.
        /// </summary>
        public long Conversations { get; set; } = 0;

        /// <summary>
        /// Messages.
        /// </summary>
        public long Messages { get; set; } = 0;

        /// <summary>
        /// Members.
        /// </summary>
        public long ActiveMembers { get; set; } = 0;

        /// <summary>
        /// Visitors.
        /// </summary>
        public long Visitors { get; set; } = 0;

        /// <summary>
        /// Up.
        /// </summary>
        public long ThumbsUp { get; set; } = 0;

        /// <summary>
        /// Down.
        /// </summary>
        public long ThumbsDown { get; set; } = 0;

        /// <summary>
        /// Up rate.
        /// </summary>
        public double? ThumbsUpRate { get; set; } = null;

        /// <summary>
        /// Model calls.
        /// </summary>
        public long Requests { get; set; } = 0;

        /// <summary>
        /// Errors.
        /// </summary>
        public long Errors { get; set; } = 0;

        /// <summary>
        /// Error rate.
        /// </summary>
        public double ErrorRate { get; set; } = 0;

        /// <summary>
        /// Prompt tokens.
        /// </summary>
        public long PromptTokens { get; set; } = 0;

        /// <summary>
        /// Completion tokens.
        /// </summary>
        public long CompletionTokens { get; set; } = 0;

        /// <summary>
        /// Cost in US dollars.
        /// </summary>
        public double CostUsd { get; set; } = 0;

        /// <summary>
        /// Latency p50.
        /// </summary>
        public long? LatencyP50Ms { get; set; } = null;

        /// <summary>
        /// Latency p95.
        /// </summary>
        public long? LatencyP95Ms { get; set; } = null;

        /// <summary>
        /// Average time to first token.
        /// </summary>
        public long? TtftAverageMs { get; set; } = null;

        /// <summary>
        /// Fallback replies over replies.
        /// </summary>
        public double FallbackRate { get; set; } = 0;

        /// <summary>
        /// Replies withheld.
        /// </summary>
        public long BlockedOutputs { get; set; } = 0;

        /// <summary>
        /// Failed replies.
        /// </summary>
        public long FailedReplies { get; set; } = 0;

        /// <summary>
        /// Property budget.
        /// </summary>
        public double? MonthlyBudgetUsd { get; set; } = null;

        /// <summary>
        /// Property spend this UTC month.
        /// </summary>
        public double MonthToDateCostUsd { get; set; } = 0;

        #endregion
    }
}
