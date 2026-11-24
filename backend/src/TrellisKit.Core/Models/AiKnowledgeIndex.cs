namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// State of an assistant's vector index in the vector store.
    /// </summary>
    public class AiKnowledgeIndex
    {
        #region Public-Members

        /// <summary>
        /// Assistant id.
        /// </summary>
        public string AssistantId { get; set; } = String.Empty;

        /// <summary>
        /// Embedding endpoint used.
        /// </summary>
        public string? EndpointId { get; set; } = null;

        /// <summary>
        /// Vector store collection id.
        /// </summary>
        public string? CollectionId { get; set; } = null;

        /// <summary>
        /// Embedding dimensions.
        /// </summary>
        public int? Dimensions { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public AiKnowledgeIndexStatusEnum Status { get; set; } = AiKnowledgeIndexStatusEnum.NotConfigured;

        /// <summary>
        /// Indexed items.
        /// </summary>
        public int ItemCount { get; set; } = 0;

        /// <summary>
        /// Stored chunks.
        /// </summary>
        public int ChunkCount { get; set; } = 0;

        /// <summary>
        /// Last pass start.
        /// </summary>
        public DateTime? LastRunUtc { get; set; } = null;

        /// <summary>
        /// Last successful pass.
        /// </summary>
        public DateTime? LastSuccessUtc { get; set; } = null;

        /// <summary>
        /// Sanitized error of the last failed pass.
        /// </summary>
        public string? LastError { get; set; } = null;

        /// <summary>
        /// Whether a full reindex was requested.
        /// </summary>
        public bool ReindexRequested { get; set; } = false;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
