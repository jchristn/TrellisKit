namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Enumeration collections of the AI models and assistants feature (docs/AI_CONTRACT.md). Aliases: aiendpoints e,
    /// aiendpointhealthchecks h, aiassistants a, aiassistantmodels am, aiconversations c, aimessages m, aifeedback f,
    /// aiknowledgeitems k, aidailyusage aggregate d.
    /// </summary>
    public static class AiCollections
    {
        #region Public-Members

        /// <summary>Bounded engine tool registry, with property-specific kill switches.</summary>
        public static readonly EnumerationCollection Tools = new EnumerationCollection("admin-ai-tools", null, "name", "createdutc", true) { HasInlineMetadata = true }
            .Order("NameAscending", "name", false, EnumerationKeyKindEnum.Text).Filters("disabled").DefaultTo("NameAscending");

        /// <summary>Provider model catalog, scoped to its endpoint and configured source bound.</summary>
        public static readonly EnumerationCollection ProviderModels = new EnumerationCollection("admin-ai-provider-models", null, "name", "createdutc", true) { HasInlineMetadata = true }
            .Order("NameAscending", "name", false, EnumerationKeyKindEnum.Text).DefaultTo("NameAscending");

        /// <summary>AI role catalog, paged by stable role key.</summary>
        public static readonly EnumerationCollection AdminRoles = new EnumerationCollection("admin-ai-roles", null, "id", "createdutc", true) { HasInlineMetadata = true };

        /// <summary>Harness runtime catalog, paged by stable runtime key.</summary>
        public static readonly EnumerationCollection AdminHarnesses = new EnumerationCollection("admin-ai-harnesses", null, "id", "createdutc", true) { HasInlineMetadata = true };

        /// <summary>Prompt catalog, paged by stable prompt key.</summary>
        public static readonly EnumerationCollection AdminPrompts = new EnumerationCollection("admin-ai-prompts", null, "id", "createdutc", true) { HasInlineMetadata = true };


        /// <summary>
        /// POST /api/v1/admin/ai/endpoints/enumerate. Orderings NameAscending (default), CreatedDescending,
        /// CreatedAscending, UpdatedDescending, HealthStatus (Unhealthy first). Search over name, description, model,
        /// and endpoint URL. Filters kind, apiType, enabled, healthStatus, authType, scope.
        /// </summary>
        public static readonly EnumerationCollection Endpoints = new EnumerationCollection("admin-ai-endpoints", TargetTypeEnum.ModelEndpoint, "e.id", "e.createdutc", true)
            .Order("NameAscending", "lower(e.name)", false, EnumerationKeyKindEnum.Text)
            .Order("UpdatedDescending", "e.updatedutc", true, EnumerationKeyKindEnum.Time)
            .Order("HealthStatus", "(CASE e.healthstatus WHEN 'Unhealthy' THEN 0 WHEN 'Unknown' THEN 1 ELSE 2 END)", false, EnumerationKeyKindEnum.Number)
            .Search("e.name", "e.description", "e.model", "e.endpointurl")
            .Filters("kind", "apiType", "enabled", "healthStatus", "authType", "scope")
            .DefaultTo("NameAscending");

        /// <summary>
        /// POST /api/v1/admin/ai/endpoints/{id}/health/enumerate: health check records, newest first (time window
        /// filters createdAfterUtc and createdBeforeUtc apply to the check time). Filter success.
        /// </summary>
        public static readonly EnumerationCollection HealthChecks = new EnumerationCollection("admin-ai-health", null, "h.id", "h.checkedutc", true)
            .Filters("success");

        /// <summary>
        /// POST /api/v1/admin/ai/endpoints/{id}/usage/enumerate: assistants that reference an endpoint, one row per
        /// purpose. Ordering NameAscending (default).
        /// </summary>
        public static readonly EnumerationCollection EndpointUsage = new EnumerationCollection("admin-ai-endpoint-usage", null, "am.assistantid || ':' || am.purpose", "a.createdutc", true)
            .Order("NameAscending", "lower(a.name)", false, EnumerationKeyKindEnum.Text)
            .DefaultTo("NameAscending");

        /// <summary>
        /// Versions of one AI prompt template (admin), newest first.
        /// </summary>
        public static readonly EnumerationCollection AdminPromptVersions = new EnumerationCollection("admin-ai-prompt-versions", TargetTypeEnum.AiPromptVersion, "v.id", "v.createdutc", true)
            .DefaultTo("CreatedDescending");

        /// <summary>
        /// POST /api/v1/admin/ai/calls/enumerate: the call ledger, newest first. Filters role, purpose, outcome,
        /// endpointId, jobKey, jobRunId, assistantId.
        /// </summary>
        public static readonly EnumerationCollection AdminCalls = new EnumerationCollection("admin-ai-calls", TargetTypeEnum.AiCall, "c.id", "c.createdutc", true)
            .Filters("role", "purpose", "outcome", "endpointId", "jobKey", "jobRunId", "assistantId")
            .DefaultTo("CreatedDescending");

        /// <summary>
        /// POST /api/v1/admin/ai/assistants/enumerate. Orderings NameAscending (default), CreatedDescending,
        /// CreatedAscending, UpdatedDescending. Search over name, title, description. Filters enabled, audience,
        /// endpointId (any purpose), embedEnabled.
        /// </summary>
        public static readonly EnumerationCollection AdminAssistants = new EnumerationCollection("admin-ai-assistants", TargetTypeEnum.Assistant, "a.id", "a.createdutc", true)
            .Order("NameAscending", "lower(a.name)", false, EnumerationKeyKindEnum.Text)
            .Order("UpdatedDescending", "a.updatedutc", true, EnumerationKeyKindEnum.Time)
            .Search("a.name", "a.title", "a.description")
            .Filters("enabled", "audience", "endpointId", "embedEnabled")
            .DefaultTo("NameAscending");

        /// <summary>
        /// POST /api/v1/assistants/enumerate: enabled assistants the caller may use. Ordering NameAscending (default).
        /// Search over name, title, description.
        /// </summary>
        public static readonly EnumerationCollection Assistants = new EnumerationCollection("assistants", TargetTypeEnum.Assistant, "a.id", "a.createdutc", false)
            .Order("NameAscending", "lower(a.name)", false, EnumerationKeyKindEnum.Text)
            .Search("a.name", "a.title", "a.description")
            .DefaultTo("NameAscending");

        /// <summary>
        /// The caller's conversations (POST /api/v1/assistants/{id}/conversations/enumerate and
        /// /api/v1/assistant-conversations/enumerate). Orderings LastMessageDescending (default), CreatedDescending,
        /// CreatedAscending. Search over title and the last message preview. Filters assistantId, archived.
        /// </summary>
        public static readonly EnumerationCollection Conversations = new EnumerationCollection("assistant-conversations", TargetTypeEnum.AssistantConversation, "c.id", "c.createdutc", false)
            .Order("LastMessageDescending", "COALESCE(c.lastmessageutc, c.createdutc)", true, EnumerationKeyKindEnum.Time)
            .Search("c.title", "c.lastmessagepreview")
            .Filters("assistantId", "archived")
            .DefaultTo("LastMessageDescending");

        /// <summary>
        /// POST /api/v1/admin/ai/conversations/enumerate: every conversation of the property (admin access is audited
        /// and disclosed to members, resolved decision 7). Orderings LastMessageDescending (default), CreatedDescending,
        /// CreatedAscending. Search over title and last message preview. Filters assistantId, userId, visitorId,
        /// ownerType (Member, Visitor), status, archived, surface.
        /// </summary>
        public static readonly EnumerationCollection AdminConversations = new EnumerationCollection("admin-ai-conversations", TargetTypeEnum.AssistantConversation, "c.id", "c.createdutc", true)
            .Order("LastMessageDescending", "COALESCE(c.lastmessageutc, c.createdutc)", true, EnumerationKeyKindEnum.Time)
            .Search("c.title", "c.lastmessagepreview")
            .Filters("assistantId", "userId", "visitorId", "ownerType", "status", "archived", "surface")
            .DefaultTo("LastMessageDescending");

        /// <summary>
        /// Messages of one conversation. Orderings CreatedAscending (default) and CreatedDescending.
        /// </summary>
        public static readonly EnumerationCollection Messages = new EnumerationCollection("assistant-messages", TargetTypeEnum.AssistantMessage, "m.id", "m.createdutc", false)
            .DefaultTo("CreatedAscending");

        /// <summary>
        /// Messages of one conversation for administrators (with telemetry). Orderings CreatedAscending (default) and
        /// CreatedDescending.
        /// </summary>
        public static readonly EnumerationCollection AdminMessages = new EnumerationCollection("admin-ai-messages", TargetTypeEnum.AssistantMessage, "m.id", "m.createdutc", true)
            .DefaultTo("CreatedAscending");

        /// <summary>
        /// Messages of a moderation snapshot (docs/AI_CONTRACT.md section 12.3). Orderings CreatedAscending (default) and
        /// CreatedDescending.
        /// </summary>
        public static readonly EnumerationCollection SnapshotMessages = new EnumerationCollection("ai-snapshot-messages", TargetTypeEnum.AssistantMessage, "s.messageid", "s.createdutc", true)
            .DefaultTo("CreatedAscending");

        /// <summary>
        /// POST /api/v1/admin/ai/feedback/enumerate. Ordering CreatedDescending (default). Filters assistantId, rating,
        /// hasNote, reviewed.
        /// </summary>
        public static readonly EnumerationCollection Feedback = new EnumerationCollection("admin-ai-feedback", null, "f.id", "f.createdutc", true)
            .Search("f.note")
            .Filters("assistantId", "rating", "hasNote", "reviewed");

        /// <summary>
        /// POST /api/v1/admin/ai/assistants/{id}/knowledge/items/enumerate: ingestion state of the assistant's knowledge
        /// items, most recently updated first (the created window applies to the update time). Search over title.
        /// Filters status, targetType.
        /// </summary>
        public static readonly EnumerationCollection KnowledgeItems = new EnumerationCollection("admin-ai-knowledge-items", null, "k.targettype || ':' || k.targetid", "k.updatedutc", true)
            .Search("k.title", "k.targetid")
            .Filters("status", "targetType");

        /// <summary>
        /// POST /api/v1/admin/ai/analytics/endpoints/enumerate: usage per endpoint over a window. Orderings NameAscending
        /// (default), RequestsDescending, CostDescending.
        /// </summary>
        public static readonly EnumerationCollection EndpointAnalytics = new EnumerationCollection("admin-ai-endpoint-analytics", null, "e.id", "e.createdutc", true)
            .Order("NameAscending", "lower(e.name)", false, EnumerationKeyKindEnum.Text)
            .Search("e.name", "e.model")
            .Filters("windowDays", "assistantId")
            .DefaultTo("NameAscending");

        /// <summary>
        /// POST /api/v1/admin/ai/analytics/assistants/enumerate: usage per assistant over a window. Ordering NameAscending
        /// (default).
        /// </summary>
        public static readonly EnumerationCollection AssistantAnalytics = new EnumerationCollection("admin-ai-assistant-analytics", null, "a.id", "a.createdutc", true)
            .Order("NameAscending", "lower(a.name)", false, EnumerationKeyKindEnum.Text)
            .Search("a.name")
            .Filters("windowDays")
            .DefaultTo("NameAscending");

        /// <summary>
        /// POST /api/v1/admin/ai/analytics/members/enumerate: messages, tokens, and cost per member or visitor over a
        /// window. Orderings CostDescending (default), MessagesDescending. Filters windowDays, assistantId, subjectType.
        /// </summary>
        public static readonly EnumerationCollection MemberAnalytics = new EnumerationCollection("admin-ai-member-analytics", null, "d.subjectkey", "d.lastdate", true)
            .Order("CostDescending", "d.costmicros", true, EnumerationKeyKindEnum.Number)
            .Order("MessagesDescending", "d.messages", true, EnumerationKeyKindEnum.Number)
            .Filters("windowDays", "assistantId", "subjectType")
            .DefaultTo("CostDescending");

        #endregion
    }
}
