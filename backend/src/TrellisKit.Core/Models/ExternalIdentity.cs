namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A linked external sign-in (Apple, Google, or Facebook). The subject is unique per provider.
    /// </summary>
    public class ExternalIdentity
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix eid_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Provider.
        /// </summary>
        public ExternalProviderEnum Provider { get; set; } = ExternalProviderEnum.Apple;

        /// <summary>
        /// Stable provider user identifier.
        /// </summary>
        public string Subject { get; set; } = String.Empty;

        /// <summary>
        /// Email the provider reported, or null.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Whether the provider asserted the email is verified.
        /// </summary>
        public bool EmailVerified { get; set; } = false;

        /// <summary>
        /// Linked (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last sign-in with this identity (UTC), or null.
        /// </summary>
        public DateTime? LastUsedUtc { get; set; } = null;

        #endregion
    }
}
