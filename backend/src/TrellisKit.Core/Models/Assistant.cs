namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An assistant (docs/AI_CONTRACT.md section 5).
    /// </summary>
    public class Assistant
    {
        #region Public-Members

        /// <summary>
        /// Owning property id.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Identifier (ast_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Name, unique per property.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description shown in lists.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Whether members may chat.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Chat header title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Avatar emoji.
        /// </summary>
        public string? AvatarEmoji { get; set; } = null;

        /// <summary>
        /// Avatar media id.
        /// </summary>
        public string? AvatarMediaId { get; set; } = null;

        /// <summary>
        /// System prompt with {tokens}.
        /// </summary>
        public string SystemPrompt { get; set; } = String.Empty;

        /// <summary>
        /// Override of the endpoint temperature.
        /// </summary>
        public double? Temperature { get; set; } = null;

        /// <summary>
        /// Override of the endpoint topP.
        /// </summary>
        public double? TopP { get; set; } = null;

        /// <summary>
        /// Who may chat.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Endpoint id per purpose.
        /// </summary>
        public Dictionary<AssistantPurposeEnum, string> Models { get; set; } = new Dictionary<AssistantPurposeEnum, string>();

        /// <summary>
        /// Whether knowledge grounding runs.
        /// </summary>
        public bool KnowledgeEnabled { get; set; } = false;

        /// <summary>
        /// Retrieval mode.
        /// </summary>
        public AssistantKnowledgeModeEnum KnowledgeMode { get; set; } = AssistantKnowledgeModeEnum.Keyword;

        /// <summary>
        /// Most knowledge items in the context.
        /// </summary>
        public int MaxContextItems { get; set; } = 6;

        /// <summary>
        /// Longest knowledge context.
        /// </summary>
        public int MaxContextCharacters { get; set; } = 12000;

        /// <summary>
        /// Whether replies carry citations.
        /// </summary>
        public bool Citations { get; set; } = true;

        /// <summary>
        /// Knowledge sources, in order.
        /// </summary>
        public List<AssistantKnowledgeSource> Sources { get; set; } = new List<AssistantKnowledgeSource>();

        /// <summary>
        /// Whether the assistant may call tools (docs/AI_CONTRACT.md section 8).
        /// </summary>
        public bool ToolsEnabled { get; set; } = false;

        /// <summary>
        /// Canonical names of the registered tools the assistant may use.
        /// </summary>
        public List<string> AllowedTools { get; set; } = new List<string>();

        /// <summary>
        /// Most model rounds that may request tools in one turn. Default 4, minimum 1, maximum 10.
        /// </summary>
        public int MaxToolIterations { get; set; } = 4;

        /// <summary>
        /// Most tool calls executed in one turn. Default 6, minimum 1, maximum 12.
        /// </summary>
        public int MaxToolCallsPerTurn { get; set; } = 6;

        /// <summary>
        /// Time limit of one tool call in milliseconds. Default 15000, minimum 100, maximum 60000.
        /// </summary>
        public int ToolCallTimeoutMs { get; set; } = 15000;

        /// <summary>
        /// Most characters of one tool output handed to the model. Default 8000, minimum 200, maximum 100000.
        /// </summary>
        public int MaxToolOutputCharacters { get; set; } = 8000;

        /// <summary>
        /// Total prompt and completion tokens of every model round in one turn after which no more tools are offered
        /// (the final answer still runs). Default 32000; 0 means no extra cap; otherwise 1000 to 1000000.
        /// </summary>
        public int MaxTokensPerTurn { get; set; } = 32000;

        /// <summary>
        /// Whether the stream emits tool.started and tool.completed events. Default true.
        /// </summary>
        public bool ShowToolActivity { get; set; } = true;

        /// <summary>
        /// Daily member allowance; 0 is unlimited.
        /// </summary>
        public int MessagesPerMemberPerDay { get; set; } = 50;

        /// <summary>
        /// Daily visitor allowance; 0 is unlimited.
        /// </summary>
        public int MessagesPerVisitorPerDay { get; set; } = 10;

        /// <summary>
        /// Messages (both roles) per conversation.
        /// </summary>
        public int MaxMessagesPerConversation { get; set; } = 100;

        /// <summary>
        /// Output token cap per reply.
        /// </summary>
        public int MaxTokensPerReply { get; set; } = 800;

        /// <summary>
        /// Longest member message.
        /// </summary>
        public int MaxMessageCharacters { get; set; } = 4000;

        /// <summary>
        /// Idle conversations are deleted after this many days.
        /// </summary>
        public int ConversationRetentionDays { get; set; } = 180;

        /// <summary>
        /// Whether replies stream.
        /// </summary>
        public bool Streaming { get; set; } = true;

        /// <summary>
        /// Whether reasoning is streamed and stored.
        /// </summary>
        public bool ShowReasoning { get; set; } = false;

        /// <summary>
        /// Starter prompts, in order.
        /// </summary>
        public List<string> StarterPrompts { get; set; } = new List<string>();

        /// <summary>
        /// Disclaimer under the composer.
        /// </summary>
        public string? Disclaimer { get; set; } = null;

        /// <summary>
        /// Whether the widget may embed the assistant (Public only).
        /// </summary>
        public bool EmbedEnabled { get; set; } = false;

        /// <summary>
        /// Extra origins allowed to embed the widget.
        /// </summary>
        public List<string> EmbedOrigins { get; set; } = new List<string>();

        /// <summary>
        /// Widget launcher label.
        /// </summary>
        public string? EmbedLauncherLabel { get; set; } = null;

        /// <summary>
        /// Widget position: BottomRight or BottomLeft.
        /// </summary>
        public string EmbedPosition { get; set; } = "BottomRight";

        /// <summary>
        /// Moderation model behavior.
        /// </summary>
        public AssistantModerationModeEnum ModerationMode { get; set; } = AssistantModerationModeEnum.Block;

        /// <summary>
        /// Monthly spend budget in US dollars, or null for none.
        /// </summary>
        public double? MonthlyBudgetUsd { get; set; } = null;

        /// <summary>
        /// Whether chat stops when the monthly budget is spent.
        /// </summary>
        public bool BudgetHardStop { get; set; } = false;

        /// <summary>
        /// Soft-delete time.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Creator.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
