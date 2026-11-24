namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A vector search result.
    /// </summary>
    public class VectorHit
    {
        #region Public-Members

        /// <summary>
        /// Chunk key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Content key.
        /// </summary>
        public string DocumentId { get; set; } = String.Empty;

        /// <summary>
        /// Chunk text.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Similarity score (higher is closer).
        /// </summary>
        public double Score { get; set; } = 0;

        /// <summary>
        /// Metadata.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
