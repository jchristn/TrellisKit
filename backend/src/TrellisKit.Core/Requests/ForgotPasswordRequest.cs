namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/auth/password/forgot.
    /// </summary>
    public class ForgotPasswordRequest
    {
        #region Public-Members

        /// <summary>
        /// Email address.
        /// </summary>
        public string? Email { get; set; } = null;

        #endregion
    }
}
