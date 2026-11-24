namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Chunks of a text and the chunker that produced them.
    /// </summary>
    public class TextChunkResult
    {
        #region Public-Members

        /// <summary>
        /// Chunker name (partio or local).
        /// </summary>
        public string Chunker { get; set; } = String.Empty;

        /// <summary>
        /// Chunks in order.
        /// </summary>
        public List<string> Chunks { get; set; } = new List<string>();

        #endregion
    }
}
