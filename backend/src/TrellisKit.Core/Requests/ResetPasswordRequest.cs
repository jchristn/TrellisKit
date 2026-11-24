namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/auth/password/reset.
    /// </summary>
    public class ResetPasswordRequest
    {
        #region Public-Members

        /// <summary>
        /// Token from the emailed link.
        /// </summary>
        public string? Token { get; set; } = null;

        /// <summary>
        /// New password.
        /// </summary>
        public string? NewPassword { get; set; } = null;

        #endregion
    }
}
