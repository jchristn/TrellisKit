namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A member's acceptance of one version of a property document (table propertydocumentacceptances; docs/TRELLISKIT.md
    /// section 14). Acceptance records are kept after account deletion as legal records.
    /// </summary>
    public class DocumentAcceptance
    {
        #region Public-Members

        /// <summary>
        /// Identifier prefix of document acceptances.
        /// </summary>
        public const string IdPrefix = "dac_";

        /// <summary>
        /// Identifier (dac_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Document key.
        /// </summary>
        public string DocumentKey { get; set; } = String.Empty;

        /// <summary>
        /// Accepted version identifier (dcv_).
        /// </summary>
        public string VersionId { get; set; } = String.Empty;

        /// <summary>
        /// Accepted display version.
        /// </summary>
        public string Version { get; set; } = String.Empty;

        /// <summary>
        /// Accepted version's sequence.
        /// </summary>
        public int Sequence { get; set; } = 1;

        /// <summary>
        /// Title of the accepted version.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// How it was accepted.
        /// </summary>
        public DocumentAcceptanceMethodEnum Method { get; set; } = DocumentAcceptanceMethodEnum.Signup;

        /// <summary>
        /// Client it was accepted from.
        /// </summary>
        public DocumentClientEnum Client { get; set; } = DocumentClientEnum.Other;

        /// <summary>
        /// When it was accepted (UTC).
        /// </summary>
        public DateTime AcceptedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the record was created (UTC; equal to AcceptedUtc).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
