namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST and PUT /api/v1/admin/ai/assistants (docs/AI_CONTRACT.md section 5.2). On update nested objects merge;
    /// knowledge.sources, starterPrompts, and embed.allowedOrigins replace their lists when present.
    /// </summary>
    public class AssistantRequest
    {
        #region Public-Members

        /// <summary>
        /// Name (1 to 100, unique).
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description (up to 1000).
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Enabled.
        /// </summary>
        public bool? Enabled { get; set; } = null;

        /// <summary>
        /// Title (1 to 80; default name).
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// mediaId or emoji.
        /// </summary>
        public Dictionary<string, JsonElement>? Avatar { get; set; } = null;

        /// <summary>
        /// System prompt with {name}, {shortName}, {memberDisplayName}, {date}, {assistantTitle}.
        /// </summary>
        public string? SystemPrompt { get; set; } = null;

        /// <summary>
        /// Endpoint id per purpose: chat (required), chatFallback, titleGeneration, queryRewrite, embedding, rerank, moderation.
        /// </summary>
        public Dictionary<string, JsonElement>? Models { get; set; } = null;

        /// <summary>
        /// temperature, topP.
        /// </summary>
        public Dictionary<string, JsonElement>? Generation { get; set; } = null;

        /// <summary>
        /// Public, SignedIn, or Members.
        /// </summary>
        public ContentAudienceEnum? Audience { get; set; } = null;

        /// <summary>
        /// enabled, mode, maxContextItems, maxContextCharacters, citations, sources.
        /// </summary>
        public Dictionary<string, JsonElement>? Knowledge { get; set; } = null;

        /// <summary>
        /// Tool policy (enabled, allowed, maxToolIterations, maxToolCallsPerTurn, toolCallTimeoutMs,
        /// maxToolOutputCharacters, maxTokensPerTurn, showToolActivity); allowed replaces the list when present.
        /// </summary>
        public Dictionary<string, JsonElement>? Tools { get; set; } = null;

        /// <summary>
        /// messagesPerMemberPerDay, messagesPerVisitorPerDay, maxMessagesPerConversation, maxTokensPerReply, maxMessageCharacters, conversationRetentionDays.
        /// </summary>
        public Dictionary<string, JsonElement>? Limits { get; set; } = null;

        /// <summary>
        /// Stream replies.
        /// </summary>
        public bool? Streaming { get; set; } = null;

        /// <summary>
        /// Show reasoning.
        /// </summary>
        public bool? ShowReasoning { get; set; } = null;

        /// <summary>
        /// Up to 6 prompts of 1 to 200 characters.
        /// </summary>
        public List<string>? StarterPrompts { get; set; } = null;

        /// <summary>
        /// Disclaimer (up to 500).
        /// </summary>
        public string? Disclaimer { get; set; } = null;

        /// <summary>
        /// enabled, allowedOrigins, launcherLabel, position.
        /// </summary>
        public Dictionary<string, JsonElement>? Embed { get; set; } = null;

        /// <summary>
        /// Off, LogOnly, or Block.
        /// </summary>
        public AssistantModerationModeEnum? ModerationMode { get; set; } = null;

        /// <summary>
        /// monthlyUsd (null for none), hardStop.
        /// </summary>
        public Dictionary<string, JsonElement>? Budget { get; set; } = null;

        /// <summary>
        /// Replacement labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Replacement tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
