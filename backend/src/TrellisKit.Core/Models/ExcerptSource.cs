namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A stored body and its stored plain-text excerpt, read to recompute excerpts after the plain-text rules change.
    /// </summary>
    public class ExcerptSource
    {
        #region Public-Members

        /// <summary>
        /// Row identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Markdown body (empty when the row has none).
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Title, for rows whose excerpt falls back to the title when the body is empty; otherwise null.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Stored excerpt.
        /// </summary>
        public string Excerpt { get; set; } = String.Empty;

        #endregion
    }
}
