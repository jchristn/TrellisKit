namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Ingestion state of one knowledge item.
    /// </summary>
    public class KnowledgeItemView
    {
        #region Public-Members

        /// <summary>
        /// Content type.
        /// </summary>
        public string TargetType { get; set; } = String.Empty;

        /// <summary>
        /// Content id.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Source.
        /// </summary>
        public string? SourceId { get; set; } = null;

        /// <summary>
        /// Title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Audience at index time.
        /// </summary>
        public string? Audience { get; set; } = null;

        /// <summary>
        /// Chunks.
        /// </summary>
        public int ChunkCount { get; set; } = 0;

        /// <summary>
        /// Status.
        /// </summary>
        public AiKnowledgeItemStatusEnum Status { get; set; } = AiKnowledgeItemStatusEnum.Pending;

        /// <summary>
        /// Error.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Indexed.
        /// </summary>
        public DateTime? IndexedUtc { get; set; } = null;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
