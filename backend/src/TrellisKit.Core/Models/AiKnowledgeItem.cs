namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Ingestion state of one content item in an assistant's vector index.
    /// </summary>
    public class AiKnowledgeItem
    {
        #region Public-Members

        /// <summary>
        /// Assistant id.
        /// </summary>
        public string AssistantId { get; set; } = String.Empty;

        /// <summary>
        /// Content type.
        /// </summary>
        public string TargetType { get; set; } = String.Empty;

        /// <summary>
        /// Content id.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Knowledge source that selected it.
        /// </summary>
        public string? SourceId { get; set; } = null;

        /// <summary>
        /// Title at index time.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// SHA-256 of the indexed text.
        /// </summary>
        public string? ContentHash { get; set; } = null;

        /// <summary>
        /// Content audience at index time.
        /// </summary>
        public string? Audience { get; set; } = null;

        /// <summary>
        /// Chunks stored.
        /// </summary>
        public int ChunkCount { get; set; } = 0;

        /// <summary>
        /// Status.
        /// </summary>
        public AiKnowledgeItemStatusEnum Status { get; set; } = AiKnowledgeItemStatusEnum.Pending;

        /// <summary>
        /// Sanitized error.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Last indexed time.
        /// </summary>
        public DateTime? IndexedUtc { get; set; } = null;

        /// <summary>
        /// Updated time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
