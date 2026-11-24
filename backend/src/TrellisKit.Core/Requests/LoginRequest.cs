namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/auth/login.
    /// </summary>
    public class LoginRequest
    {
        #region Public-Members

        /// <summary>
        /// Email address.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Password.
        /// </summary>
        public string? Password { get; set; } = null;

        #endregion
    }
}
