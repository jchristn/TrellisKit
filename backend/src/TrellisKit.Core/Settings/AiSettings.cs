namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// AI models and assistants settings (docs/AI_CONTRACT.md section 17). Endpoint credentials are never settings: they are stored encrypted per endpoint.
    /// </summary>
    public class AiSettings
    {
        #region Public-Members

        /// <summary>
        /// Deployment-wide switch for every AI route and worker (features.ai in property.json must also be on). Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Comma-separated apiType values endpoints may use; empty allows every supported type. Default empty. Null is stored as empty.
        /// </summary>
        public string EnabledApiTypes
        {
            get { return _EnabledApiTypes; }
            set { _EnabledApiTypes = value ?? String.Empty; }
        }

        /// <summary>
        /// Whether administrators may reveal stored endpoint secrets. Default true.
        /// </summary>
        public bool AllowSecretReveal { get; set; } = true;

        /// <summary>
        /// A reveal needs a member session signed in within this many minutes. Default 15. Minimum 1, maximum 1440.
        /// </summary>
        public int RevealRecentSignInMinutes
        {
            get { return _RevealRecentSignInMinutes; }
            set { _RevealRecentSignInMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Whether PolyPrompt records call metadata (never bodies) for diagnostics. Default false.
        /// </summary>
        public bool CaptureCallDetails { get; set; } = false;

        /// <summary>
        /// Most extra headers per endpoint. Default 10. Minimum 0, maximum 50.
        /// </summary>
        public int MaxExtraHeaders
        {
            get { return _MaxExtraHeaders; }
            set { _MaxExtraHeaders = Math.Clamp(value, 0, 50); }
        }

        /// <summary>
        /// Context window in tokens used when an endpoint sets none. Default 8192. Minimum 512, maximum 2000000.
        /// </summary>
        public int DefaultContextWindow
        {
            get { return _DefaultContextWindow; }
            set { _DefaultContextWindow = Math.Clamp(value, 512, 2000000); }
        }

        /// <summary>
        /// Longest assistant system prompt. Default 20000. Minimum 1000, maximum 200000.
        /// </summary>
        public int MaxSystemPromptCharacters
        {
            get { return _MaxSystemPromptCharacters; }
            set { _MaxSystemPromptCharacters = Math.Clamp(value, 1000, 200000); }
        }

        /// <summary>
        /// Most provider models read into the model list cache. Default 1000. Minimum 10, maximum 10000.
        /// </summary>
        public int MaxListedModels
        {
            get { return _MaxListedModels; }
            set { _MaxListedModels = Math.Clamp(value, 10, 10000); }
        }

        /// <summary>
        /// How long a provider model list is cached. Default 10. Minimum 1, maximum 1440.
        /// </summary>
        public int ModelListCacheMinutes
        {
            get { return _ModelListCacheMinutes; }
            set { _ModelListCacheMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Inputs per embedding call during ingestion. Default 32. Minimum 1, maximum 512.
        /// </summary>
        public int EmbeddingBatchSize
        {
            get { return _EmbeddingBatchSize; }
            set { _EmbeddingBatchSize = Math.Clamp(value, 1, 512); }
        }

        /// <summary>
        /// Days of health check records kept. Default 7. Minimum 1, maximum 90.
        /// </summary>
        public int HealthHistoryDays
        {
            get { return _HealthHistoryDays; }
            set { _HealthHistoryDays = Math.Clamp(value, 1, 90); }
        }

        /// <summary>
        /// Most health check records kept per endpoint. Default 2000. Minimum 100, maximum 100000.
        /// </summary>
        public int HealthHistoryMaxRecords
        {
            get { return _HealthHistoryMaxRecords; }
            set { _HealthHistoryMaxRecords = Math.Clamp(value, 100, 100000); }
        }

        /// <summary>
        /// Window of the uptime percentage. Default 24. Minimum 1, maximum 720.
        /// </summary>
        public int UptimeWindowHours
        {
            get { return _UptimeWindowHours; }
            set { _UptimeWindowHours = Math.Clamp(value, 1, 720); }
        }

        /// <summary>
        /// Whether conversation titles come from a model (otherwise the start of the first message). Default true.
        /// </summary>
        public bool GenerateTitles { get; set; } = true;

        /// <summary>
        /// Whether members see the model, token counts, and timings of replies. Default false.
        /// </summary>
        public bool ShowModelDetailsToMembers { get; set; } = false;

        /// <summary>
        /// Seconds between SSE keep-alive comments. Default 15. Minimum 5, maximum 60.
        /// </summary>
        public int SseHeartbeatSeconds
        {
            get { return _SseHeartbeatSeconds; }
            set { _SseHeartbeatSeconds = Math.Clamp(value, 5, 60); }
        }

        /// <summary>
        /// Most knowledge sources per assistant. Default 10. Minimum 1, maximum 50.
        /// </summary>
        public int MaxKnowledgeSources
        {
            get { return _MaxKnowledgeSources; }
            set { _MaxKnowledgeSources = Math.Clamp(value, 1, 50); }
        }

        /// <summary>
        /// Vector search candidates fetched before the visibility re-check. Default 50. Minimum 5, maximum 500.
        /// </summary>
        public int VectorCandidates
        {
            get { return _VectorCandidates; }
            set { _VectorCandidates = Math.Clamp(value, 5, 500); }
        }

        /// <summary>
        /// Minutes between knowledge ingestion passes. Default 15. Minimum 1, maximum 1440.
        /// </summary>
        public int KnowledgeIndexIntervalMinutes
        {
            get { return _KnowledgeIndexIntervalMinutes; }
            set { _KnowledgeIndexIntervalMinutes = Math.Clamp(value, 1, 1440); }
        }

        /// <summary>
        /// Days visitor conversations are kept. Default 30. Minimum 1, maximum 365.
        /// </summary>
        public int VisitorRetentionDays
        {
            get { return _VisitorRetentionDays; }
            set { _VisitorRetentionDays = Math.Clamp(value, 1, 365); }
        }

        /// <summary>
        /// Days rows of the per-call AI ledger are kept (pruned by the ai.callsPrune job). Default 90, minimum 7, maximum 3650.
        /// </summary>
        public int CallRetentionDays
        {
            get { return _CallRetentionDays; }
            set { _CallRetentionDays = Math.Clamp(value, 7, 3650); }
        }

        /// <summary>
        /// Whether publishing an AI prompt draft needs a complete, passing live run of the scope benchmark
        /// (ai.scopeBenchmark) on that exact draft, in addition to the deterministic scope gate that always runs
        /// (docs/INTEGRATION.md owner requirement R7, docs/BENCHMARKS.md). Default true.
        /// </summary>
        public bool PromptPublishRequiresBenchmark { get; set; } = true;

        /// <summary>
        /// Days a deleted assistant's conversations are kept. Default 30. Minimum 0, maximum 365.
        /// </summary>
        public int DeletedAssistantRetentionDays
        {
            get { return _DeletedAssistantRetentionDays; }
            set { _DeletedAssistantRetentionDays = Math.Clamp(value, 0, 365); }
        }

        /// <summary>
        /// Days feedback rows are kept. Default 365. Minimum 30, maximum 3650.
        /// </summary>
        public int FeedbackRetentionDays
        {
            get { return _FeedbackRetentionDays; }
            set { _FeedbackRetentionDays = Math.Clamp(value, 30, 3650); }
        }

        /// <summary>
        /// Days usage rollups are kept. Default 400. Minimum 30, maximum 3650.
        /// </summary>
        public int AnalyticsRetentionDays
        {
            get { return _AnalyticsRetentionDays; }
            set { _AnalyticsRetentionDays = Math.Clamp(value, 30, 3650); }
        }

        /// <summary>
        /// Where the mobile app shows assistants: a menu entry, or a tab. Default Menu.
        /// </summary>
        public TrellisKit.Core.Enums.AiMobilePlacementEnum MobilePlacement { get; set; } = TrellisKit.Core.Enums.AiMobilePlacementEnum.Menu;

        /// <summary>
        /// Property-wide monthly model spend budget in whole US dollars; 0 means no budget. Default 0. Minimum 0, maximum 100000000.
        /// </summary>
        public int MonthlyBudgetUsd
        {
            get { return _MonthlyBudgetUsd; }
            set { _MonthlyBudgetUsd = Math.Clamp(value, 0, 100000000); }
        }

        /// <summary>
        /// Percent of a budget that sends the first alert email (a second alert is sent at 100). Default 80. Minimum 1, maximum 100.
        /// </summary>
        public int BudgetAlertPercent
        {
            get { return _BudgetAlertPercent; }
            set { _BudgetAlertPercent = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Whether member chat stops when the property-wide budget is spent. Default false.
        /// </summary>
        public bool BudgetHardStop { get; set; } = false;

        /// <summary>
        /// Administrative email address for budget alerts; empty uses the property support email. Default empty. Null is stored as empty.
        /// </summary>
        public string BudgetAlertEmail
        {
            get { return _BudgetAlertEmail; }
            set { _BudgetAlertEmail = value ?? String.Empty; }
        }

        /// <summary>
        /// RecallDB base URL for vector retrieval, for example http://recalldb:8600; empty disables vector retrieval. Default empty. Null is stored as empty.
        /// </summary>
        public string RecallDbEndpoint
        {
            get { return _RecallDbEndpoint; }
            set { _RecallDbEndpoint = value ?? String.Empty; }
        }

        /// <summary>
        /// RecallDB bearer token (a secret). Default empty. Null is stored as empty.
        /// </summary>
        public string RecallDbToken
        {
            get { return _RecallDbToken; }
            set { _RecallDbToken = value ?? String.Empty; }
        }

        /// <summary>
        /// RecallDB tenant that holds the knowledge collections. Default "default". Null is stored as empty.
        /// </summary>
        public string RecallDbTenantId
        {
            get { return _RecallDbTenantId; }
            set { _RecallDbTenantId = value ?? String.Empty; }
        }

        /// <summary>
        /// Member messages (send and regenerate) per minute. Default 10. Minimum 1, maximum 1000.
        /// </summary>
        public int MessagesPerMinute
        {
            get { return _MessagesPerMinute; }
            set { _MessagesPerMinute = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Visitor messages per minute. Default 5. Minimum 1, maximum 1000.
        /// </summary>
        public int VisitorMessagesPerMinute
        {
            get { return _VisitorMessagesPerMinute; }
            set { _VisitorMessagesPerMinute = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Assistant tool calls per member each hour (across assistants). Default 200. Minimum 1, maximum 100000.
        /// </summary>
        public int ToolCallsPerMemberPerHour
        {
            get { return _ToolCallsPerMemberPerHour; }
            set { _ToolCallsPerMemberPerHour = Math.Clamp(value, 1, 100000); }
        }

        /// <summary>
        /// Assistant tool calls per visitor each hour. Default 40. Minimum 1, maximum 100000.
        /// </summary>
        public int ToolCallsPerVisitorPerHour
        {
            get { return _ToolCallsPerVisitorPerHour; }
            set { _ToolCallsPerVisitorPerHour = Math.Clamp(value, 1, 100000); }
        }

        /// <summary>
        /// Classify tool outputs with the assistant's moderation model (Block mode) before they reach the chat model.
        /// Default false (tool outputs are content the caller can already open, which passed the app's own moderation).
        /// </summary>
        public bool ModerateToolResults { get; set; } = false;

        /// <summary>
        /// Visitor messages per IP per hour. Default 30. Minimum 1, maximum 100000.
        /// </summary>
        public int VisitorMessagesPerIpPerHour
        {
            get { return _VisitorMessagesPerIpPerHour; }
            set { _VisitorMessagesPerIpPerHour = Math.Clamp(value, 1, 100000); }
        }

        /// <summary>
        /// New visitors per IP per hour. Default 10. Minimum 1, maximum 10000.
        /// </summary>
        public int NewVisitorsPerIpPerHour
        {
            get { return _NewVisitorsPerIpPerHour; }
            set { _NewVisitorsPerIpPerHour = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// New conversations per caller per hour. Default 30. Minimum 1, maximum 10000.
        /// </summary>
        public int ConversationsPerHour
        {
            get { return _ConversationsPerHour; }
            set { _ConversationsPerHour = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Feedback writes per member per hour. Default 60. Minimum 1, maximum 10000.
        /// </summary>
        public int FeedbackPerHour
        {
            get { return _FeedbackPerHour; }
            set { _FeedbackPerHour = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Endpoint tests, validations, model listings, and health checks per admin per hour. Default 30. Minimum 1, maximum 10000.
        /// </summary>
        public int AdminTestsPerHour
        {
            get { return _AdminTestsPerHour; }
            set { _AdminTestsPerHour = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Secret reveals per admin per hour. Default 10. Minimum 1, maximum 1000.
        /// </summary>
        public int RevealsPerHour
        {
            get { return _RevealsPerHour; }
            set { _RevealsPerHour = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Assistant test console exchanges per admin per hour. Default 60. Minimum 1, maximum 10000.
        /// </summary>
        public int AssistantTestsPerHour
        {
            get { return _AssistantTestsPerHour; }
            set { _AssistantTestsPerHour = Math.Clamp(value, 1, 10000); }
        }

        #endregion

        #region Private-Members

        private string _EnabledApiTypes = String.Empty;
        private int _RevealRecentSignInMinutes = 15;
        private int _MaxExtraHeaders = 10;
        private int _DefaultContextWindow = 8192;
        private int _MaxSystemPromptCharacters = 20000;
        private int _MaxListedModels = 1000;
        private int _ModelListCacheMinutes = 10;
        private int _EmbeddingBatchSize = 32;
        private int _HealthHistoryDays = 7;
        private int _HealthHistoryMaxRecords = 2000;
        private int _UptimeWindowHours = 24;
        private int _SseHeartbeatSeconds = 15;
        private int _MaxKnowledgeSources = 10;
        private int _VectorCandidates = 50;
        private int _KnowledgeIndexIntervalMinutes = 15;
        private int _VisitorRetentionDays = 30;
        private int _CallRetentionDays = 90;
        private int _DeletedAssistantRetentionDays = 30;
        private int _FeedbackRetentionDays = 365;
        private int _AnalyticsRetentionDays = 400;
        private int _MonthlyBudgetUsd = 0;
        private int _BudgetAlertPercent = 80;
        private string _BudgetAlertEmail = String.Empty;
        private string _RecallDbEndpoint = String.Empty;
        private string _RecallDbToken = String.Empty;
        private string _RecallDbTenantId = "default";
        private int _MessagesPerMinute = 10;
        private int _VisitorMessagesPerMinute = 5;
        private int _VisitorMessagesPerIpPerHour = 30;
        private int _ToolCallsPerMemberPerHour = 200;
        private int _ToolCallsPerVisitorPerHour = 40;
        private int _NewVisitorsPerIpPerHour = 10;
        private int _ConversationsPerHour = 30;
        private int _FeedbackPerHour = 60;
        private int _AdminTestsPerHour = 30;
        private int _RevealsPerHour = 10;
        private int _AssistantTestsPerHour = 60;

        #endregion
    }
}
