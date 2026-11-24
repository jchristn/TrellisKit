namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/admin/documents/{key}/versions: publish a new current version.
    /// </summary>
    public class DocumentPublishRequest
    {
        #region Public-Members

        /// <summary>
        /// Display version, 1 to 32 characters, not used by an earlier version of the document.
        /// </summary>
        public string? Version { get; set; } = null;

        /// <summary>
        /// Markdown content, 1 to property.maxDocumentCharacters characters.
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// What changed, 1 to 500 characters.
        /// </summary>
        public string? ChangeSummary { get; set; } = null;

        /// <summary>
        /// New title (1 to 120 characters), or null to keep the current title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Whether members must accept this version (gating documents only), or null for the document's reacceptOnChange.
        /// </summary>
        public bool? RequireReacceptance { get; set; } = null;

        #endregion
    }
}
