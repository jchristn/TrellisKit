namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Verified claims from an external identity provider token.
    /// </summary>
    public class ExternalIdentityClaims
    {
        #region Public-Members

        /// <summary>
        /// Provider that issued the token.
        /// </summary>
        public ExternalProviderEnum Provider { get; set; } = ExternalProviderEnum.Apple;

        /// <summary>
        /// Stable provider user identifier (the token's sub, or the Facebook user id).
        /// </summary>
        public string Subject { get; set; } = String.Empty;

        /// <summary>
        /// Email address, if the provider shared one. Null when withheld.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Whether the provider asserts the email is verified.
        /// </summary>
        public bool EmailVerified { get; set; } = false;

        /// <summary>
        /// Given name, if provided.
        /// </summary>
        public string? GivenName { get; set; } = null;

        /// <summary>
        /// Family name, if provided.
        /// </summary>
        public string? FamilyName { get; set; } = null;

        /// <summary>
        /// Full display name, if provided.
        /// </summary>
        public string? DisplayName { get; set; } = null;

        #endregion
    }
}
