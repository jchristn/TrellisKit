namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Usage of one assistant over an analytics window.
    /// </summary>
    public class AiAssistantAnalyticsItem
    {
        #region Public-Members

        /// <summary>
        /// The assistant.
        /// </summary>
        public AiAssistantRef Assistant { get; set; } = new AiAssistantRef();

        /// <summary>
        /// Conversations created.
        /// </summary>
        public long Conversations { get; set; } = 0;

        /// <summary>
        /// Member and visitor messages.
        /// </summary>
        public long Messages { get; set; } = 0;

        /// <summary>
        /// Up ratings over all ratings.
        /// </summary>
        public double? ThumbsUpRate { get; set; } = null;

        /// <summary>
        /// Model calls.
        /// </summary>
        public long Requests { get; set; } = 0;

        /// <summary>
        /// Errors over requests.
        /// </summary>
        public double ErrorRate { get; set; } = 0;

        /// <summary>
        /// 95th percentile latency.
        /// </summary>
        public long? LatencyP95Ms { get; set; } = null;

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
        /// Monthly budget.
        /// </summary>
        public double? MonthlyBudgetUsd { get; set; } = null;

        /// <summary>
        /// Spend this UTC month.
        /// </summary>
        public double MonthToDateCostUsd { get; set; } = 0;

        #endregion
    }
}
