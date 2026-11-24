namespace TrellisKit.Core.Database.Postgresql.Implementations
{
    using System;
    using TrellisKit.Core.Database.Interfaces;

    /// <summary>
    /// PostgreSQL AI method sets, bound to one property (or unbound on the deployment driver, where every call throws).
    /// </summary>
    internal sealed class AiMethods : IAiMethods
    {
        public IAiEndpointMethods Endpoints { get; }

        public IAiAssistantMethods Assistants { get; }

        public IAiConversationMethods Conversations { get; }

        public IAiUsageMethods Usage { get; }

        /// <inheritdoc />
        public IAiModerationMethods Moderation { get; }

        /// <inheritdoc />
        public IAiPromptMethods Prompts { get; }

        /// <inheritdoc />
        public IAiRoleMethods Roles { get; }

        /// <inheritdoc />
        public IAiCallMethods Calls { get; }

        public IAiHarnessMethods Harnesses { get; }

        internal AiMethods(DatabaseDriverBase driver, string? propertyId)
        {
            ArgumentNullException.ThrowIfNull(driver);
            Endpoints = new AiEndpointMethods(driver, propertyId);
            Assistants = new AiAssistantMethods(driver, propertyId);
            Conversations = new AiConversationMethods(driver, propertyId);
            Usage = new AiUsageMethods(driver, propertyId);
            Moderation = new AiModerationMethods(driver, propertyId);
            Prompts = new AiPromptMethods(driver, propertyId);
            Roles = new AiRoleMethods(driver, propertyId);
            Calls = new AiCallMethods(driver, propertyId);
            Harnesses = new AiHarnessMethods(driver, propertyId);
        }
    }
}
