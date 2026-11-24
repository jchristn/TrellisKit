namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Usage of one endpoint over an analytics window.
    /// </summary>
    public class AiEndpointAnalyticsItem
    {
        #region Public-Members

        /// <summary>
        /// The endpoint.
        /// </summary>
        public AiEndpointRef Endpoint { get; set; } = new AiEndpointRef();

        /// <summary>
        /// Model calls.
        /// </summary>
        public long Requests { get; set; } = 0;

        /// <summary>
        /// Failed calls.
        /// </summary>
        public long Errors { get; set; } = 0;

        /// <summary>
        /// Errors over requests.
        /// </summary>
        public double ErrorRate { get; set; } = 0;

        /// <summary>
        /// 95th percentile latency from the histogram.
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
        /// Requests per purpose.
        /// </summary>
        public Dictionary<string, long> Purposes { get; set; } = new Dictionary<string, long>();

        #endregion
    }
}
