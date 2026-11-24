namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Endpoint ids per purpose (null when unset).
    /// </summary>
    public class AssistantModelsView
    {
        #region Public-Members

        /// <summary>
        /// Chat (required).
        /// </summary>
        public string? Chat { get; set; } = null;

        /// <summary>
        /// Fallback chat.
        /// </summary>
        public string? ChatFallback { get; set; } = null;

        /// <summary>
        /// Titles.
        /// </summary>
        public string? TitleGeneration { get; set; } = null;

        /// <summary>
        /// Query rewrite.
        /// </summary>
        public string? QueryRewrite { get; set; } = null;

        /// <summary>
        /// Embeddings.
        /// </summary>
        public string? Embedding { get; set; } = null;

        /// <summary>
        /// Rerank.
        /// </summary>
        public string? Rerank { get; set; } = null;

        /// <summary>
        /// Moderation classifier.
        /// </summary>
        public string? Moderation { get; set; } = null;

        /// <summary>
        /// Tool routing: decides tool calls before the chat model answers.
        /// </summary>
        public string? ToolRouting { get; set; } = null;

        #endregion
    }
}
