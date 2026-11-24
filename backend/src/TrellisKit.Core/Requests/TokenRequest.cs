namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body carrying a single emailed token, for example POST /api/v1/auth/email/verify.
    /// </summary>
    public class TokenRequest
    {
        #region Public-Members

        /// <summary>
        /// Token from the emailed link.
        /// </summary>
        public string? Token { get; set; } = null;

        #endregion
    }
}
