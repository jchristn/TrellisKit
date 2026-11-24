namespace TrellisKit.Server.Services
{
    using System;

    /// <summary>
    /// A newly issued access token.
    /// </summary>
    public class IssuedAccessToken
    {
        #region Public-Members

        /// <summary>
        /// Opaque token.
        /// </summary>
        public string Token { get; set; } = String.Empty;

        /// <summary>
        /// Expiry (UTC).
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
