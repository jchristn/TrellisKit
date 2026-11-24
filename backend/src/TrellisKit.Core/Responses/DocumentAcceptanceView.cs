namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A member's acceptance of a property document version.
    /// </summary>
    public class DocumentAcceptanceView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Acceptance id (dac_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Document key.
        /// </summary>
        public string DocumentKey { get; set; } = String.Empty;

        /// <summary>
        /// Title of the accepted version.
        /// </summary>
        public string DocumentTitle { get; set; } = String.Empty;

        /// <summary>
        /// Accepted display version.
        /// </summary>
        public string Version { get; set; } = String.Empty;

        /// <summary>
        /// Accepted version sequence.
        /// </summary>
        public int Sequence { get; set; } = 1;

        /// <summary>
        /// Accepted version id (dcv_).
        /// </summary>
        public string VersionId { get; set; } = String.Empty;

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Member summary (staff views), or null.
        /// </summary>
        public UserSummary? User { get; set; } = null;

        /// <summary>
        /// Signup or Reacceptance.
        /// </summary>
        public DocumentAcceptanceMethodEnum Method { get; set; } = DocumentAcceptanceMethodEnum.Signup;

        /// <summary>
        /// Web, Ios, Android, or Other.
        /// </summary>
        public DocumentClientEnum Client { get; set; } = DocumentClientEnum.Other;

        /// <summary>
        /// When it was accepted (UTC).
        /// </summary>
        public DateTime AcceptedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the record was created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

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
