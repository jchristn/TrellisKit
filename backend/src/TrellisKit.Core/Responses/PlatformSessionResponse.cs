namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A platform administrator sign-in (POST /api/v1/platform/auth/login).
    /// </summary>
    public class PlatformSessionResponse
    {
        #region Public-Members

        /// <summary>
        /// Bearer token (tkp_...). Shown once.
        /// </summary>
        public string AccessToken { get; set; } = String.Empty;

        /// <summary>
        /// Absolute expiry.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The administrator.
        /// </summary>
        public PlatformAdminResponse Admin { get; set; } = new PlatformAdminResponse();

        #endregion
    }
}
