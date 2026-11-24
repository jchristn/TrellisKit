namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// A model slot of an assistant (docs/AI_CONTRACT.md section 5.2).
    /// </summary>
    public enum AssistantPurposeEnum
    {
        /// <summary>
        /// Replies.
        /// </summary>
        Chat,

        /// <summary>
        /// Replies when the chat endpoint is unavailable.
        /// </summary>
        ChatFallback,

        /// <summary>
        /// Conversation titles.
        /// </summary>
        TitleGeneration,

        /// <summary>
        /// Standalone retrieval queries.
        /// </summary>
        QueryRewrite,

        /// <summary>
        /// Vector retrieval embeddings.
        /// </summary>
        Embedding,

        /// <summary>
        /// Reranking of retrieved items.
        /// </summary>
        Rerank,

        /// <summary>
        /// Input and output classification.
        /// </summary>
        Moderation,

        /// <summary>
        /// Tool selection (reserved).
        /// </summary>
        ToolRouting,

        /// <summary>
        /// A task role call through the role gateway (jobs and platform features, docs/INTEGRATION.md).
        /// </summary>
        Task,

        /// <summary>
        /// An administrator's test or validation call of a model endpoint (ledger only; never a model slot).
        /// </summary>
        EndpointTest,

        /// <summary>
        /// A health check that calls the model (Completion, Embedding, or Rerank mode; ledger only; never a model slot).
        /// </summary>
        HealthCheck
    }
}
