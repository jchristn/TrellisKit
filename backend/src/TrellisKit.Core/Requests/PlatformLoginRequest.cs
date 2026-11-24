namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// POST /api/v1/platform/auth/login body.
    /// </summary>
    public class PlatformLoginRequest
    {
        #region Public-Members

        /// <summary>
        /// Platform administrator email.
        /// </summary>
        public string Email { get; set; } = String.Empty;

        /// <summary>
        /// Password.
        /// </summary>
        public string Password { get; set; } = String.Empty;

        #endregion
    }
}
