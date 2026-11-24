namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One chunk of extracted document text (documentchunks).
    /// </summary>
    public class DocumentChunk
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Media.
        /// </summary>
        public string MediaId { get; set; } = String.Empty;

        /// <summary>
        /// Position (0-based).
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Page number (PDF), or null.
        /// </summary>
        public int? PageNumber { get; set; } = null;

        /// <summary>
        /// Text.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
