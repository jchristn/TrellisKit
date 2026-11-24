namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One published version of a property document (table propertydocumentversions; docs/TRELLISKIT.md section 14).
    /// Versions are immutable once stored; the current version of a document is the one with the highest sequence.
    /// </summary>
    public class PropertyDocumentVersion
    {
        #region Public-Members

        /// <summary>
        /// Identifier prefix of document versions.
        /// </summary>
        public const string IdPrefix = "dcv_";

        /// <summary>
        /// Identifier (dcv_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Document key (property.json documents[].key).
        /// </summary>
        public string DocumentKey { get; set; } = String.Empty;

        /// <summary>
        /// Publish counter of the document, starting at 1 and increasing by one per version.
        /// </summary>
        public int Sequence { get; set; } = 1;

        /// <summary>
        /// Display version, for example 1.0 (unique per document, case-insensitive).
        /// </summary>
        public string Version { get; set; } = String.Empty;

        /// <summary>
        /// Title of the document in this version.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Markdown content.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Summary of what changed, or null (the seed has none).
        /// </summary>
        public string? ChangeSummary { get; set; } = null;

        /// <summary>
        /// Whether members who accepted an earlier version must accept this one (re-acceptance prompt). The seed version
        /// always requires acceptance.
        /// </summary>
        public bool RequiresReacceptance { get; set; } = true;

        /// <summary>
        /// How the version was created.
        /// </summary>
        public DocumentVersionSourceEnum Source { get; set; } = DocumentVersionSourceEnum.Seed;

        /// <summary>
        /// Hash of the property folder's version and file content when this version was stored (an administrator's version
        /// carries the hash of the version it replaced), used to detect changes on disk. Null when unknown.
        /// </summary>
        public string? SeedHash { get; set; } = null;

        /// <summary>
        /// Member or platform administrator who published it, or null for the seed and directory sync.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// When it was published (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Content length in characters (filled when content or its length was read).
        /// </summary>
        public int ContentLength { get; set; } = 0;

        /// <summary>
        /// Distinct members who accepted this version (filled by enumerations; 0 otherwise).
        /// </summary>
        public long AcceptanceCount { get; set; } = 0;

        #endregion
    }
}
