namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One version of a property document (admin version history). Content is filled on single reads.
    /// </summary>
    public class DocumentVersionView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Version id (dcv_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Document key.
        /// </summary>
        public string DocumentKey { get; set; } = String.Empty;

        /// <summary>
        /// Display version.
        /// </summary>
        public string Version { get; set; } = String.Empty;

        /// <summary>
        /// Publish counter (1-based).
        /// </summary>
        public int Sequence { get; set; } = 1;

        /// <summary>
        /// Title in this version.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// What changed, or null.
        /// </summary>
        public string? ChangeSummary { get; set; } = null;

        /// <summary>
        /// Whether members who accepted an earlier version must accept this one.
        /// </summary>
        public bool RequiresReacceptance { get; set; } = true;

        /// <summary>
        /// How it was created: Seed, Edit, or DirectorySync.
        /// </summary>
        public string Source { get; set; } = "Seed";

        /// <summary>
        /// Content length in characters.
        /// </summary>
        public int ContentLength { get; set; } = 0;

        /// <summary>
        /// Who published it, or null.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// When it was published (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Distinct members who accepted this version.
        /// </summary>
        public long AcceptanceCount { get; set; } = 0;

        /// <summary>
        /// Markdown content (single reads), or null.
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// Labels (always present).
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags (always present).
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        #endregion
    }
}
