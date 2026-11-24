namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A host name registered to a property. Hosts are unique across the deployment (they are the resolution key).
    /// </summary>
    public class PropertyDomain
    {
        #region Public-Members

        /// <summary>
        /// Identifier (pdm_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Owning property.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Lowercase host name without port.
        /// </summary>
        public string Host { get; set; } = String.Empty;

        /// <summary>
        /// Role of the host.
        /// </summary>
        public DomainKindEnum Kind { get; set; } = DomainKindEnum.Web;

        /// <summary>
        /// When ownership was verified. Domains registered by a platform administrator or seeded from the property folder are verified on registration.
        /// </summary>
        public DateTime? VerifiedUtc { get; set; } = null;

        /// <summary>
        /// Who registered the domain: Platform (platform admins, the property folder; verified on registration) or
        /// Property (a property administrator; verified by DNS TXT record).
        /// </summary>
        public string Source { get; set; } = "Platform";

        /// <summary>
        /// Ownership challenge value expected in the TXT record, or null.
        /// </summary>
        public string? VerificationToken { get; set; } = null;

        /// <summary>
        /// Last verification check (UTC), or null.
        /// </summary>
        public DateTime? VerificationCheckedUtc { get; set; } = null;

        /// <summary>
        /// Why the last check failed, or null.
        /// </summary>
        public string? VerificationError { get; set; } = null;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
