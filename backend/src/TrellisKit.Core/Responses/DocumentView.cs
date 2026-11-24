namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/public/documents/{key}: a property document's current version with its Markdown content.
    /// </summary>
    public class DocumentView : DocumentSummaryView
    {
        #region Public-Members

        /// <summary>
        /// Markdown content.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// What changed in the current version, or null.
        /// </summary>
        public string? ChangeSummary { get; set; } = null;

        #endregion
    }
}
