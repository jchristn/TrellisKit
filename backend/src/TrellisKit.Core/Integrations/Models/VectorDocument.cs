namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One chunk stored in the vector store.
    /// </summary>
    public class VectorDocument
    {
        #region Public-Members

        /// <summary>
        /// Unique key in the collection: targetType:targetId:chunkIndex.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Content key shared by the chunks of one item: targetType:targetId.
        /// </summary>
        public string DocumentId { get; set; } = String.Empty;

        /// <summary>
        /// Chunk index.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Chunk text.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Embedding.
        /// </summary>
        public float[] Embedding { get; set; } = System.Array.Empty<float>();

        /// <summary>
        /// Metadata (targetType, targetId, title, path, audience).
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
