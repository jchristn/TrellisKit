namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// What non-administrators see of an assistant (docs/AI_CONTRACT.md section 6.1).
    /// </summary>
    public class AssistantSummaryView
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
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Avatar.
        /// </summary>
        public AssistantAvatarView Avatar { get; set; } = new AssistantAvatarView();

        /// <summary>
        /// Starter prompts.
        /// </summary>
        public List<string> StarterPrompts { get; set; } = new List<string>();

        /// <summary>
        /// Disclaimer.
        /// </summary>
        public string? Disclaimer { get; set; } = null;

        /// <summary>
        /// Streaming.
        /// </summary>
        public bool Streaming { get; set; } = true;

        /// <summary>
        /// Show reasoning.
        /// </summary>
        public bool ShowReasoning { get; set; } = false;

        /// <summary>
        /// Audience.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Whether the assistant may call tools for this caller.
        /// </summary>
        public bool ToolsEnabled { get; set; } = false;

        /// <summary>
        /// Knowledge grounding.
        /// </summary>
        public bool KnowledgeEnabled { get; set; } = false;

        /// <summary>
        /// Limits for the caller.
        /// </summary>
        public AssistantMemberLimitsView Limits { get; set; } = new AssistantMemberLimitsView();

        /// <summary>
        /// Privacy disclosure (resolved decision 7).
        /// </summary>
        public AssistantPrivacyView Privacy { get; set; } = new AssistantPrivacyView();

        /// <summary>
        /// Widget presentation (embeddable public assistants only).
        /// </summary>
        public AssistantEmbedSummaryView? Embed { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
