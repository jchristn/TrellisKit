namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Call parameters of an endpoint.
    /// </summary>
    public class EndpointParametersView
    {
        #region Public-Members

        /// <summary>
        /// Temperature.
        /// </summary>
        public double? Temperature { get; set; } = null;

        /// <summary>
        /// Top p.
        /// </summary>
        public double? TopP { get; set; } = null;

        /// <summary>
        /// Output token cap.
        /// </summary>
        public int? MaxTokens { get; set; } = null;

        /// <summary>
        /// Context window.
        /// </summary>
        public int? ContextWindow { get; set; } = null;

        /// <summary>
        /// Reasoning effort.
        /// </summary>
        public ModelReasoningEffortEnum? ReasoningEffort { get; set; } = null;

        /// <summary>
        /// Streaming.
        /// </summary>
        public bool Streaming { get; set; } = true;

        /// <summary>
        /// How assistant tools reach the model: Native, Prompt, or Off.
        /// </summary>
        public ModelToolCallingEnum ToolCalling { get; set; } = ModelToolCallingEnum.Native;

        /// <summary>
        /// Whole-call timeout.
        /// </summary>
        public int TimeoutMs { get; set; } = 120000;

        /// <summary>
        /// First token timeout.
        /// </summary>
        public int? FirstResponseTimeoutMs { get; set; } = null;

        /// <summary>
        /// Embedding dimensions.
        /// </summary>
        public int? Dimensions { get; set; } = null;

        #endregion
    }
}
