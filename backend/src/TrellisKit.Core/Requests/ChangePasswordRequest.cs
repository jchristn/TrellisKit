namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of PUT /api/v1/me/password.
    /// </summary>
    public class ChangePasswordRequest
    {
        #region Public-Members

        /// <summary>
        /// Current password; required when one is set.
        /// </summary>
        public string? CurrentPassword { get; set; } = null;

        /// <summary>
        /// New password.
        /// </summary>
        public string? NewPassword { get; set; } = null;

        #endregion
    }
}
