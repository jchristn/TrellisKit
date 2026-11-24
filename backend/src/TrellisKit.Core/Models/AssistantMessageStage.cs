namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Telemetry of one stage of a reply (moderation, rewrite, retrieval, rerank, chat, title).
    /// </summary>
    public class AssistantMessageStage
    {
        #region Public-Members

        /// <summary>
        /// Order.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Stage name, for example retrieval or chat.
        /// </summary>
        public string Stage { get; set; } = String.Empty;

        /// <summary>
        /// Endpoint used.
        /// </summary>
        public string? EndpointId { get; set; } = null;

        /// <summary>
        /// Model used.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Start time.
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Duration.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Whether the stage succeeded.
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// Short detail (decision, counts, error code); never message content.
        /// </summary>
        public string? Detail { get; set; } = null;

        /// <summary>
        /// Prompt tokens of the stage's model call.
        /// </summary>
        public int? PromptTokens { get; set; } = null;

        /// <summary>
        /// Completion tokens of the stage's model call.
        /// </summary>
        public int? CompletionTokens { get; set; } = null;

        #endregion
    }
}
