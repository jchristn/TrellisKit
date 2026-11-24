namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Ingestion status of an assistant's vector index.
    /// </summary>
    public class KnowledgeIndexView
    {
        #region Public-Members

        /// <summary>
        /// Status.
        /// </summary>
        public AiKnowledgeIndexStatusEnum Status { get; set; } = AiKnowledgeIndexStatusEnum.NotConfigured;

        /// <summary>
        /// Whether RecallDB is configured.
        /// </summary>
        public bool VectorStoreConfigured { get; set; } = false;

        /// <summary>
        /// Whether the assistant has an embedding endpoint.
        /// </summary>
        public bool EmbeddingConfigured { get; set; } = false;

        /// <summary>
        /// Embedding endpoint.
        /// </summary>
        public string? EndpointId { get; set; } = null;

        /// <summary>
        /// Vector store collection.
        /// </summary>
        public string? CollectionId { get; set; } = null;

        /// <summary>
        /// Dimensions.
        /// </summary>
        public int? Dimensions { get; set; } = null;

        /// <summary>
        /// Indexed items.
        /// </summary>
        public int ItemCount { get; set; } = 0;

        /// <summary>
        /// Stored chunks.
        /// </summary>
        public int ChunkCount { get; set; } = 0;

        /// <summary>
        /// Last pass.
        /// </summary>
        public DateTime? LastRunUtc { get; set; } = null;

        /// <summary>
        /// Last successful pass.
        /// </summary>
        public DateTime? LastSuccessUtc { get; set; } = null;

        /// <summary>
        /// Last error.
        /// </summary>
        public string? LastError { get; set; } = null;

        /// <summary>
        /// Reindex requested.
        /// </summary>
        public bool ReindexRequested { get; set; } = false;

        #endregion
    }
}
