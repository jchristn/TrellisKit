namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A short-lived, audited session in which a platform administrator acts as SystemAdmin inside one property.
    /// </summary>
    public class DelegatedSessionResponse
    {
        #region Public-Members

        /// <summary>
        /// Access token, valid only for PropertyId. Not refreshable.
        /// </summary>
        public string AccessToken { get; set; } = String.Empty;

        /// <summary>
        /// Expiry (platform.delegatedSessionMinutes).
        /// </summary>
        public DateTime AccessTokenExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The property the session is bound to; send it as X-TK-Property.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        #endregion
    }
}
