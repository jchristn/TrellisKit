namespace TrellisKit.Core.Database.Interfaces
{
    /// <summary>
    /// Data access of the AI models and assistants feature (docs/AI_CONTRACT.md section 18), grouped by area. Every set
    /// is bound to the same property as the driver that exposes it.
    /// </summary>
    public interface IAiMethods
    {
        /// <summary>
        /// Model endpoints, headers, and health history.
        /// </summary>
        IAiEndpointMethods Endpoints { get; }

        /// <summary>
        /// Assistants, their child lists, and knowledge ingestion state.
        /// </summary>
        IAiAssistantMethods Assistants { get; }

        /// <summary>
        /// Visitors, conversations, messages, feedback, and daily usage.
        /// </summary>
        IAiConversationMethods Conversations { get; }

        /// <summary>
        /// Usage rollups, cost, budgets, and analytics.
        /// </summary>
        IAiUsageMethods Usage { get; }

        /// <summary>
        /// Moderation snapshots of assistant conversations.
        /// </summary>
        IAiModerationMethods Moderation { get; }

        /// <summary>
        /// Administrator-editable prompt templates and their versions.
        /// </summary>
        IAiPromptMethods Prompts { get; }

        /// <summary>
        /// Task role executor hierarchies.
        /// </summary>
        IAiRoleMethods Roles { get; }

        /// <summary>
        /// The per-call ledger.
        /// </summary>
        IAiCallMethods Calls { get; }

        /// <summary>
        /// Harness profiles.
        /// </summary>
        IAiHarnessMethods Harnesses { get; }
    }
}
