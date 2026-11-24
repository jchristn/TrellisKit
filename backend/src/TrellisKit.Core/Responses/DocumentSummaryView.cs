namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A property document with its current version (docs/TRELLISKIT.md section 14): GET /api/v1/property documents[], MeResponse pendingDocuments[], and the base of the document views.
    /// </summary>
    public class DocumentSummaryView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Document key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Title of the current version.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// What the document is.
        /// </summary>
        public DocumentKindEnum Kind { get; set; } = DocumentKindEnum.Other;

        /// <summary>
        /// How members accept it.
        /// </summary>
        public DocumentAcceptanceModeEnum Acceptance { get; set; } = DocumentAcceptanceModeEnum.InformationOnly;

        /// <summary>
        /// Whether sign-up shows its own required checkbox.
        /// </summary>
        public bool SeparateCheckbox { get; set; } = false;

        /// <summary>
        /// Whether members accept new versions before continuing.
        /// </summary>
        public bool ReacceptOnChange { get; set; } = false;

        /// <summary>
        /// Public website path segment.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Whether the website footer links it.
        /// </summary>
        public bool Footer { get; set; } = true;

        /// <summary>
        /// Meta description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Display version of the current version.
        /// </summary>
        public string Version { get; set; } = String.Empty;

        /// <summary>
        /// Publish counter of the current version (1-based).
        /// </summary>
        public int Sequence { get; set; } = 1;

        /// <summary>
        /// When the current version was published (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// On MeResponse pendingDocuments only: true when the member accepted an earlier version that this version
        /// replaces (an updated document), false when they never accepted the document. Omitted elsewhere.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? PreviouslyAccepted { get; set; } = null;

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
