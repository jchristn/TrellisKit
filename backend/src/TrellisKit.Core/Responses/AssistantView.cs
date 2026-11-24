namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Admin payload of an assistant (docs/AI_CONTRACT.md section 5.1).
    /// </summary>
    public class AssistantView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Chat title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Avatar.
        /// </summary>
        public AssistantAvatarView Avatar { get; set; } = new AssistantAvatarView();

        /// <summary>
        /// System prompt.
        /// </summary>
        public string SystemPrompt { get; set; } = String.Empty;

        /// <summary>
        /// Endpoint ids per purpose.
        /// </summary>
        public AssistantModelsView Models { get; set; } = new AssistantModelsView();

        /// <summary>
        /// Overrides of temperature and topP.
        /// </summary>
        public AssistantGenerationView Generation { get; set; } = new AssistantGenerationView();

        /// <summary>
        /// Audience.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Knowledge grounding.
        /// </summary>
        public AssistantKnowledgeView Knowledge { get; set; } = new AssistantKnowledgeView();

        /// <summary>
        /// Tool policy (section 8).
        /// </summary>
        public AssistantToolsView Tools { get; set; } = new AssistantToolsView();

        /// <summary>
        /// Limits.
        /// </summary>
        public AssistantLimitsView Limits { get; set; } = new AssistantLimitsView();

        /// <summary>
        /// Streaming.
        /// </summary>
        public bool Streaming { get; set; } = true;

        /// <summary>
        /// Show reasoning.
        /// </summary>
        public bool ShowReasoning { get; set; } = false;

        /// <summary>
        /// Starter prompts.
        /// </summary>
        public List<string> StarterPrompts { get; set; } = new List<string>();

        /// <summary>
        /// Disclaimer.
        /// </summary>
        public string? Disclaimer { get; set; } = null;

        /// <summary>
        /// Widget settings.
        /// </summary>
        public AssistantEmbedView Embed { get; set; } = new AssistantEmbedView();

        /// <summary>
        /// Moderation mode.
        /// </summary>
        public AssistantModerationModeEnum ModerationMode { get; set; } = AssistantModerationModeEnum.Block;

        /// <summary>
        /// Monthly budget.
        /// </summary>
        public AssistantBudgetView Budget { get; set; } = new AssistantBudgetView();

        /// <summary>
        /// 30-day statistics.
        /// </summary>
        public AssistantStatsView Stats { get; set; } = new AssistantStatsView();

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
