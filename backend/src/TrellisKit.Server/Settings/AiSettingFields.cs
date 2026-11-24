namespace TrellisKit.Server.Settings
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The AI models and assistants settings (docs/AI_CONTRACT.md section 17) and the engine secrets key, appended to
    /// <see cref="SettingsRegistry.All"/>. Endpoint credentials are never settings: they are stored encrypted per endpoint.
    /// </summary>
    public static class AiSettingFields
    {
        #region Public-Methods

        /// <summary>
        /// Build the AI setting definitions in display order.
        /// </summary>
        /// <returns>Definitions.</returns>
        public static List<SettingFieldDefinition> Build()
        {
            const SettingApplyModeEnum live = SettingApplyModeEnum.Live;

            return new List<SettingFieldDefinition>
            {
                SettingFieldDefinition.Boolean("ai.enabled", "AI", "AI enabled",
                    "Deployment-wide switch for every AI route and worker. A property also needs features.ai in property.json.", live, "TK_AI_ENABLED",
                    s => s.Ai.Enabled, (s, v) => s.Ai.Enabled = v),
                SettingFieldDefinition.Text("ai.enabledApiTypes", "AI", "Enabled provider API types",
                    "Comma-separated apiType values endpoints may use (for example Ollama,OpenAI,Gemini,Tei). Empty allows every supported type.", live, "TK_AI_ENABLED_API_TYPES",
                    s => s.Ai.EnabledApiTypes, (s, v) => s.Ai.EnabledApiTypes = (v ?? String.Empty).Trim()),
                SettingFieldDefinition.Boolean("ai.allowSecretReveal", "AI", "Allow secret reveal",
                    "Whether administrators may reveal stored endpoint credentials (audited, recent sign-in required).", live, "TK_AI_ALLOW_SECRET_REVEAL",
                    s => s.Ai.AllowSecretReveal, (s, v) => s.Ai.AllowSecretReveal = v),
                SettingFieldDefinition.Integer("ai.revealRecentSignInMinutes", "AI", "Reveal sign-in window (minutes)",
                    "A reveal needs a sign-in within this many minutes.", live, null, 1, 1440,
                    s => s.Ai.RevealRecentSignInMinutes, (s, v) => s.Ai.RevealRecentSignInMinutes = v),
                SettingFieldDefinition.Boolean("ai.captureCallDetails", "AI", "Capture model call details",
                    "Record PolyPrompt call metadata (URL, status, timing, redacted headers; never bodies) for diagnostics.", live, "TK_AI_CAPTURE_CALL_DETAILS",
                    s => s.Ai.CaptureCallDetails, (s, v) => s.Ai.CaptureCallDetails = v),
                SettingFieldDefinition.Integer("ai.maxExtraHeaders", "AI", "Extra headers per endpoint",
                    "Most extra headers an endpoint may carry.", live, null, 0, 50,
                    s => s.Ai.MaxExtraHeaders, (s, v) => s.Ai.MaxExtraHeaders = v),
                SettingFieldDefinition.Boolean("ai.moderateToolResults", "AI", "Moderate tool results",
                    "Classify assistant tool outputs with the assistant's moderation model (Block mode) before they reach the chat model; a Block verdict withholds the output.", live, null,
                    s => s.Ai.ModerateToolResults, (s, v) => s.Ai.ModerateToolResults = v),
                SettingFieldDefinition.Integer("ai.defaultContextWindow", "AI", "Default context window (tokens)",
                    "Used when an endpoint sets none; drives history trimming.", live, null, 512, 2000000,
                    s => s.Ai.DefaultContextWindow, (s, v) => s.Ai.DefaultContextWindow = v),
                SettingFieldDefinition.Integer("ai.maxSystemPromptCharacters", "AI", "Longest system prompt",
                    "Most characters in an assistant system prompt.", live, null, 1000, 200000,
                    s => s.Ai.MaxSystemPromptCharacters, (s, v) => s.Ai.MaxSystemPromptCharacters = v),
                SettingFieldDefinition.Integer("ai.maxListedModels", "AI", "Provider models listed",
                    "Most provider models read into the model list cache.", live, null, 10, 10000,
                    s => s.Ai.MaxListedModels, (s, v) => s.Ai.MaxListedModels = v),
                SettingFieldDefinition.Integer("ai.modelListCacheMinutes", "AI", "Model list cache (minutes)",
                    "How long a provider model list is cached.", live, null, 1, 1440,
                    s => s.Ai.ModelListCacheMinutes, (s, v) => s.Ai.ModelListCacheMinutes = v),
                SettingFieldDefinition.Integer("ai.embeddingBatchSize", "AI", "Embedding batch size",
                    "Inputs per embedding call.", live, null, 1, 512,
                    s => s.Ai.EmbeddingBatchSize, (s, v) => s.Ai.EmbeddingBatchSize = v),
                SettingFieldDefinition.Integer("ai.healthHistoryDays", "AI", "Health history (days)",
                    "Days of endpoint health checks kept.", live, null, 1, 90,
                    s => s.Ai.HealthHistoryDays, (s, v) => s.Ai.HealthHistoryDays = v),
                SettingFieldDefinition.Integer("ai.healthHistoryMaxRecords", "AI", "Health records per endpoint",
                    "Most health check records kept per endpoint.", live, null, 100, 100000,
                    s => s.Ai.HealthHistoryMaxRecords, (s, v) => s.Ai.HealthHistoryMaxRecords = v),
                SettingFieldDefinition.Integer("ai.uptimeWindowHours", "AI", "Uptime window (hours)",
                    "Window of the endpoint uptime percentage.", live, null, 1, 720,
                    s => s.Ai.UptimeWindowHours, (s, v) => s.Ai.UptimeWindowHours = v),
                SettingFieldDefinition.Boolean("ai.generateTitles", "AI", "Generate conversation titles",
                    "Use the title model (or chat model) for conversation titles; otherwise the start of the first message.", live, null,
                    s => s.Ai.GenerateTitles, (s, v) => s.Ai.GenerateTitles = v),
                SettingFieldDefinition.Boolean("ai.showModelDetailsToMembers", "AI", "Show model details to members",
                    "Members see which model answered, token counts, and timings.", live, null,
                    s => s.Ai.ShowModelDetailsToMembers, (s, v) => s.Ai.ShowModelDetailsToMembers = v),
                SettingFieldDefinition.Integer("ai.sseHeartbeatSeconds", "AI", "Stream heartbeat (seconds)",
                    "Seconds between keep-alive comments on reply streams.", live, null, 5, 60,
                    s => s.Ai.SseHeartbeatSeconds, (s, v) => s.Ai.SseHeartbeatSeconds = v),
                SettingFieldDefinition.Integer("ai.maxKnowledgeSources", "AI", "Knowledge sources per assistant",
                    "Most knowledge sources an assistant may have.", live, null, 1, 50,
                    s => s.Ai.MaxKnowledgeSources, (s, v) => s.Ai.MaxKnowledgeSources = v),
                SettingFieldDefinition.Integer("ai.vectorCandidates", "AI", "Vector candidates",
                    "Vector search candidates fetched before the visibility re-check.", live, null, 5, 500,
                    s => s.Ai.VectorCandidates, (s, v) => s.Ai.VectorCandidates = v),
                SettingFieldDefinition.Integer("ai.knowledgeIndexIntervalMinutes", "AI", "Knowledge ingestion interval (minutes)",
                    "Minutes between knowledge ingestion passes.", live, null, 1, 1440,
                    s => s.Ai.KnowledgeIndexIntervalMinutes, (s, v) => s.Ai.KnowledgeIndexIntervalMinutes = v),
                SettingFieldDefinition.Integer("ai.visitorRetentionDays", "AI", "Visitor conversation retention (days)",
                    "Days visitor conversations are kept.", live, null, 1, 365,
                    s => s.Ai.VisitorRetentionDays, (s, v) => s.Ai.VisitorRetentionDays = v),
                SettingFieldDefinition.Integer("ai.callRetentionDays", "AI", "Call ledger retention (days)",
                    "Days rows of the per-call AI ledger are kept.", live, null, 7, 3650,
                    s => s.Ai.CallRetentionDays, (s, v) => s.Ai.CallRetentionDays = v),
                SettingFieldDefinition.Boolean("ai.promptPublishRequiresBenchmark", "AI", "Prompt publish needs the scope benchmark",
                    "Publishing an AI prompt draft needs a complete, passing live run of the scope benchmark job (ai.scopeBenchmark) on that draft. The deterministic scope gate always runs.", live, "TK_AI_PROMPT_PUBLISH_REQUIRES_BENCHMARK",
                    s => s.Ai.PromptPublishRequiresBenchmark, (s, v) => s.Ai.PromptPublishRequiresBenchmark = v),
                SettingFieldDefinition.Integer("ai.deletedAssistantRetentionDays", "AI", "Deleted assistant retention (days)",
                    "Days a deleted assistant's conversations are kept.", live, null, 0, 365,
                    s => s.Ai.DeletedAssistantRetentionDays, (s, v) => s.Ai.DeletedAssistantRetentionDays = v),
                SettingFieldDefinition.Integer("ai.feedbackRetentionDays", "AI", "Feedback retention (days)",
                    "Days reply feedback is kept.", live, null, 30, 3650,
                    s => s.Ai.FeedbackRetentionDays, (s, v) => s.Ai.FeedbackRetentionDays = v),
                SettingFieldDefinition.Integer("ai.analyticsRetentionDays", "AI", "Analytics retention (days)",
                    "Days usage rollups are kept.", live, null, 30, 3650,
                    s => s.Ai.AnalyticsRetentionDays, (s, v) => s.Ai.AnalyticsRetentionDays = v),
                SettingFieldDefinition.Choice<AiMobilePlacementEnum>("ai.mobilePlacement", "AI", "Mobile placement",
                    "Where the mobile app shows assistants: Menu (an entry in the profile menu) or Tab (a tab of its own).", live, null,
                    s => s.Ai.MobilePlacement, (s, v) => s.Ai.MobilePlacement = v),
                SettingFieldDefinition.Integer("ai.monthlyBudgetUsd", "AI", "Monthly budget (US dollars)",
                    "Property-wide monthly model spend budget, from the prices entered per endpoint. 0 means no budget.", live, null, 0, 100000000,
                    s => s.Ai.MonthlyBudgetUsd, (s, v) => s.Ai.MonthlyBudgetUsd = v),
                SettingFieldDefinition.Integer("ai.budgetAlertPercent", "AI", "Budget alert percent",
                    "Percent of a budget that sends the first alert email (a second is sent at 100).", live, null, 1, 100,
                    s => s.Ai.BudgetAlertPercent, (s, v) => s.Ai.BudgetAlertPercent = v),
                SettingFieldDefinition.Boolean("ai.budgetHardStop", "AI", "Budget hard stop",
                    "Stop member chat when the property-wide budget is spent.", live, null,
                    s => s.Ai.BudgetHardStop, (s, v) => s.Ai.BudgetHardStop = v),
                SettingFieldDefinition.Text("ai.budgetAlertEmail", "AI", "Budget alert email",
                    "Administrative address for budget alerts; empty uses the property support email.", live, null,
                    s => s.Ai.BudgetAlertEmail, (s, v) => s.Ai.BudgetAlertEmail = (v ?? String.Empty).Trim()),
                SettingFieldDefinition.Text("ai.recallDbEndpoint", "AI", "RecallDB URL",
                    "RecallDB base URL for vector knowledge retrieval, for example http://recalldb:8600. Empty disables vector retrieval (keyword retrieval still works).", live, "TK_RECALLDB_ENDPOINT",
                    s => s.Ai.RecallDbEndpoint, (s, v) => s.Ai.RecallDbEndpoint = OptionalUrl(v)),
                SettingFieldDefinition.Secret("ai.recallDbToken", "AI", "RecallDB token",
                    "Bearer token of the RecallDB tenant credential.", live, "TK_RECALLDB_TOKEN",
                    s => s.Ai.RecallDbToken, (s, v) => s.Ai.RecallDbToken = (v ?? String.Empty).Trim()),
                SettingFieldDefinition.Text("ai.recallDbTenantId", "AI", "RecallDB tenant",
                    "RecallDB tenant that holds the knowledge collections.", live, "TK_RECALLDB_TENANT",
                    s => s.Ai.RecallDbTenantId, (s, v) => s.Ai.RecallDbTenantId = (v ?? String.Empty).Trim()),
                SettingFieldDefinition.Integer("rateLimits.aiMessagesPerMinute", "Rate limits", "AI messages per minute",
                    "Member messages (send and regenerate) per minute.", live, null, 1, 1000,
                    s => s.Ai.MessagesPerMinute, (s, v) => s.Ai.MessagesPerMinute = v),
                SettingFieldDefinition.Integer("rateLimits.aiVisitorMessagesPerMinute", "Rate limits", "AI visitor messages per minute",
                    "Visitor messages per minute.", live, null, 1, 1000,
                    s => s.Ai.VisitorMessagesPerMinute, (s, v) => s.Ai.VisitorMessagesPerMinute = v),
                SettingFieldDefinition.Integer("rateLimits.aiVisitorMessagesPerIpPerHour", "Rate limits", "AI visitor messages per IP per hour",
                    "Visitor messages per IP each hour.", live, null, 1, 100000,
                    s => s.Ai.VisitorMessagesPerIpPerHour, (s, v) => s.Ai.VisitorMessagesPerIpPerHour = v),
                SettingFieldDefinition.Integer("rateLimits.aiToolCallsPerMemberPerHour", "Rate limits", "AI tool calls per member per hour",
                    "Assistant tool calls per member each hour; further calls are rejected and the model is told so.", live, null, 1, 100000,
                    s => s.Ai.ToolCallsPerMemberPerHour, (s, v) => s.Ai.ToolCallsPerMemberPerHour = v),
                SettingFieldDefinition.Integer("rateLimits.aiToolCallsPerVisitorPerHour", "Rate limits", "AI tool calls per visitor per hour",
                    "Assistant tool calls per visitor each hour.", live, null, 1, 100000,
                    s => s.Ai.ToolCallsPerVisitorPerHour, (s, v) => s.Ai.ToolCallsPerVisitorPerHour = v),
                SettingFieldDefinition.Integer("rateLimits.aiNewVisitorsPerIpPerHour", "Rate limits", "AI new visitors per IP per hour",
                    "New visitor sessions per IP each hour.", live, null, 1, 10000,
                    s => s.Ai.NewVisitorsPerIpPerHour, (s, v) => s.Ai.NewVisitorsPerIpPerHour = v),
                SettingFieldDefinition.Integer("rateLimits.aiConversationsPerHour", "Rate limits", "AI conversations per hour",
                    "New conversations per member or visitor each hour.", live, null, 1, 10000,
                    s => s.Ai.ConversationsPerHour, (s, v) => s.Ai.ConversationsPerHour = v),
                SettingFieldDefinition.Integer("rateLimits.aiFeedbackPerHour", "Rate limits", "AI feedback per hour",
                    "Reply ratings per member each hour.", live, null, 1, 10000,
                    s => s.Ai.FeedbackPerHour, (s, v) => s.Ai.FeedbackPerHour = v),
                SettingFieldDefinition.Integer("rateLimits.aiAdminTestsPerHour", "Rate limits", "AI endpoint tests per hour",
                    "Endpoint tests, validations, model listings, and health checks per admin each hour.", live, null, 1, 10000,
                    s => s.Ai.AdminTestsPerHour, (s, v) => s.Ai.AdminTestsPerHour = v),
                SettingFieldDefinition.Integer("rateLimits.aiRevealsPerHour", "Rate limits", "AI secret reveals per hour",
                    "Secret reveals per admin each hour.", live, null, 1, 1000,
                    s => s.Ai.RevealsPerHour, (s, v) => s.Ai.RevealsPerHour = v),
                SettingFieldDefinition.Integer("rateLimits.aiAssistantTestsPerHour", "Rate limits", "AI assistant tests per hour",
                    "Assistant test console exchanges per admin each hour.", live, null, 1, 10000,
                    s => s.Ai.AssistantTestsPerHour, (s, v) => s.Ai.AssistantTestsPerHour = v)
            };
        }

        #endregion

        #region Private-Methods

        private static string OptionalUrl(string value)
        {
            string trimmed = (value ?? String.Empty).Trim().TrimEnd('/');
            if (trimmed.Length == 0) return String.Empty;
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("Must be an absolute http or https URL.");
            return trimmed;
        }

        #endregion
    }
}
