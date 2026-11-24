namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One stage of a reply.
    /// </summary>
    public class MessageStageView
    {
        #region Public-Members

        /// <summary>
        /// Stage.
        /// </summary>
        public string Stage { get; set; } = String.Empty;

        /// <summary>
        /// Endpoint.
        /// </summary>
        public string? EndpointId { get; set; } = null;

        /// <summary>
        /// Model.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Start.
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Duration.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Success.
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// Detail.
        /// </summary>
        public string? Detail { get; set; } = null;

        /// <summary>
        /// Prompt tokens.
        /// </summary>
        public int? PromptTokens { get; set; } = null;

        /// <summary>
        /// Completion tokens.
        /// </summary>
        public int? CompletionTokens { get; set; } = null;

        #endregion
    }
}
