namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A property document for the admin console (GET /api/v1/admin/documents and /{key}, publish): the summary plus where the current version came from and acceptance counts. Content and changeSummary are filled on single reads.
    /// </summary>
    public class AdminDocumentView : DocumentSummaryView
    {
        #region Public-Members

        /// <summary>
        /// How the current version was created: Seed, Edit, or DirectorySync.
        /// </summary>
        public string Source { get; set; } = "Seed";

        /// <summary>
        /// Current version id (dcv_).
        /// </summary>
        public string VersionId { get; set; } = String.Empty;

        /// <summary>
        /// Stored versions of the document.
        /// </summary>
        public long VersionCount { get; set; } = 0;

        /// <summary>
        /// Distinct members who accepted the current version.
        /// </summary>
        public long AcceptedCurrentCount { get; set; } = 0;

        /// <summary>
        /// Distinct members who accepted any version.
        /// </summary>
        public long AcceptedAnyCount { get; set; } = 0;

        /// <summary>
        /// The lowest sequence a member must have accepted (the latest version that requires re-acceptance); 0 for documents that are not accepted.
        /// </summary>
        public int RequiredSequence { get; set; } = 0;

        /// <summary>
        /// Whether the property folder replaces the document when its version or file changes on disk (SingleProperty).
        /// </summary>
        public bool SyncsFromFolder { get; set; } = false;

        /// <summary>
        /// Who published the current version, or null.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// Markdown content of the current version (single reads), or null.
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// What changed in the current version, or null.
        /// </summary>
        public string? ChangeSummary { get; set; } = null;

        #endregion
    }
}
