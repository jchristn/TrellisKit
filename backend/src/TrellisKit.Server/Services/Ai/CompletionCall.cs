namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using PolyPrompt.Models;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// One completion request through <see cref="ModelInvoker"/>.
    /// </summary>
    public class CompletionCall
    {
        #region Public-Members

        /// <summary>
        /// Primary endpoint.
        /// </summary>
        public ModelEndpoint Endpoint { get; set; } = new ModelEndpoint();

        /// <summary>
        /// Fallback endpoint (chat only), or null.
        /// </summary>
        public ModelEndpoint? Fallback { get; set; } = null;

        /// <summary>
        /// Messages: system, knowledge context, history, and the new user message.
        /// </summary>
        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

        /// <summary>
        /// Output token cap (the smaller of the endpoint and assistant caps), or null.
        /// </summary>
        public int? MaxTokens { get; set; } = null;

        /// <summary>
        /// Temperature override, or null.
        /// </summary>
        public double? Temperature { get; set; } = null;

        /// <summary>
        /// Top p override, or null.
        /// </summary>
        public double? TopP { get; set; } = null;

        /// <summary>
        /// Whether to stream (also needs the endpoint's streaming parameter).
        /// </summary>
        public bool Stream { get; set; } = false;

        /// <summary>
        /// Purpose for usage and metrics.
        /// </summary>
        public AssistantPurposeEnum Purpose { get; set; } = AssistantPurposeEnum.Chat;

        /// <summary>
        /// Assistant, or null.
        /// </summary>
        public string? AssistantId { get; set; } = null;

        /// <summary>
        /// Admin test traffic.
        /// </summary>
        public bool IsTest { get; set; } = false;

        /// <summary>
        /// Usage idempotency key (the reply id), or null.
        /// </summary>
        public string? GuardKey { get; set; } = null;

        /// <summary>
        /// Tool definitions offered this round (empty for none). Endpoints with toolCalling Native receive them as
        /// provider tools, Prompt endpoints as the JSON protocol, and Off endpoints not at all.
        /// </summary>
        public List<ToolDefinition> Tools { get; set; } = new List<ToolDefinition>();

        /// <summary>
        /// Reasoning effort overriding the endpoint's (task roles), or null.
        /// </summary>
        public TrellisKit.Core.Enums.ModelReasoningEffortEnum? ReasoningEffort { get; set; } = null;

        #endregion
    }
}
