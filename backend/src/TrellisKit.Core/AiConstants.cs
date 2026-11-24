namespace TrellisKit.Core
{
    /// <summary>
    /// Identifier prefixes, headers, and fixed bounds of the AI models and assistants feature (docs/AI_CONTRACT.md).
    /// </summary>
    public static class AiConstants
    {
        #region Public-Members

        /// <summary>
        /// Model endpoint identifier prefix.
        /// </summary>
        public const string EndpointIdPrefix = "mep_";

        /// <summary>
        /// Endpoint health check record identifier prefix.
        /// </summary>
        public const string HealthCheckIdPrefix = "mhc_";

        /// <summary>
        /// Assistant identifier prefix.
        /// </summary>
        public const string AssistantIdPrefix = "ast_";

        /// <summary>
        /// Knowledge source identifier prefix.
        /// </summary>
        public const string KnowledgeSourceIdPrefix = "aks_";

        /// <summary>
        /// Conversation identifier prefix.
        /// </summary>
        public const string ConversationIdPrefix = "acv_";

        /// <summary>
        /// Conversation message identifier prefix.
        /// </summary>
        public const string MessageIdPrefix = "acm_";

        /// <summary>
        /// Feedback identifier prefix.
        /// </summary>
        public const string FeedbackIdPrefix = "afb_";

        /// <summary>
        /// Tool call identifier prefix.
        /// </summary>
        public const string ToolCallIdPrefix = "atc_";

        /// <summary>
        /// Most tool calls stored and shown per reply (the largest per-turn cap).
        /// </summary>
        public const int MaxToolCallsPerReply = 12;

        /// <summary>
        /// Most result links kept per tool call.
        /// </summary>
        public const int MaxToolResults = 10;

        /// <summary>
        /// Most characters of tool arguments accepted and stored.
        /// </summary>
        public const int MaxToolArgumentsCharacters = 4096;

        /// <summary>
        /// Characters of a tool output kept as its summary.
        /// </summary>
        public const int ToolResultSummaryCharacters = 500;

        /// <summary>
        /// Moderation snapshot identifier prefix.
        /// </summary>
        public const string ModerationSnapshotIdPrefix = "ams_";

        /// <summary>
        /// Anonymous visitor identifier prefix.
        /// </summary>
        public const string VisitorIdPrefix = "avs_";

        /// <summary>
        /// Request and response header that carries the visitor token.
        /// </summary>
        public const string VisitorTokenHeader = "X-TK-Visitor-Token";

        /// <summary>
        /// Most extra headers an endpoint may carry regardless of settings.
        /// </summary>
        public const int MaxExtraHeadersHardLimit = 50;

        /// <summary>
        /// Most starter prompts per assistant.
        /// </summary>
        public const int MaxStarterPrompts = 6;

        /// <summary>
        /// Most embed origins per assistant.
        /// </summary>
        public const int MaxEmbedOrigins = 20;

        /// <summary>
        /// Longest endpoint test output returned, in characters.
        /// </summary>
        public const int MaxTestOutputCharacters = 2000;

        /// <summary>
        /// Default completion token cap of an endpoint test (room for reasoning models to think and still answer).
        /// </summary>
        public const int DefaultTestMaxTokens = 1024;

        /// <summary>
        /// Largest completion token cap an endpoint test accepts.
        /// </summary>
        public const int MaxTestMaxTokens = 8192;

        /// <summary>
        /// Most recent messages returned with a conversation.
        /// </summary>
        public const int RecentMessages = 20;

        /// <summary>
        /// Most messages in an admin test exchange.
        /// </summary>
        public const int MaxTestMessages = 20;

        /// <summary>
        /// Most usage references previewed in usedBy.
        /// </summary>
        public const int UsedByPreview = 10;

        /// <summary>
        /// Deployment-wide placeholder property id for Deployment-scope endpoints (reserved).
        /// </summary>
        public const string DeploymentScopePropertyId = "*";

        /// <summary>
        /// Chunk size in characters used for vector ingestion.
        /// </summary>
        public const int ChunkCharacters = 800;

        /// <summary>
        /// Overlap between consecutive chunks in characters.
        /// </summary>
        public const int ChunkOverlap = 100;

        #endregion
    }
}
