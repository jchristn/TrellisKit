namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Status of an assistant's vector index.
    /// </summary>
    public enum AiKnowledgeIndexStatusEnum
    {
        /// <summary>
        /// No embedding endpoint or vector store.
        /// </summary>
        NotConfigured,

        /// <summary>
        /// Up to date.
        /// </summary>
        Idle,

        /// <summary>
        /// An ingestion pass is running.
        /// </summary>
        Running,

        /// <summary>
        /// The last pass failed.
        /// </summary>
        Failed
    }
}
