namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A property domain in a platform payload.
    /// </summary>
    public class PlatformDomainResponse : CatalogEntryMetadata
    {
        #region Public-Members

        /// <summary>
        /// Identifier (pdm_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Host.
        /// </summary>
        public string Host { get; set; } = String.Empty;

        /// <summary>
        /// Role.
        /// </summary>
        public DomainKindEnum Kind { get; set; } = DomainKindEnum.Web;

        /// <summary>
        /// When ownership was verified.
        /// </summary>
        public DateTime? VerifiedUtc { get; set; } = null;

        /// <summary>
        /// Platform (registered by a platform admin or the property folder; verified at once) or Property (added by a
        /// property administrator; routed only after its DNS TXT check).
        /// </summary>
        public string Source { get; set; } = "Platform";

        /// <summary>
        /// The DNS TXT challenge of a domain added by a property administrator, or null.
        /// </summary>
        public DomainVerificationView? Verification { get; set; } = null;

        #endregion
    }
}
