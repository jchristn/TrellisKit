namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/signup-codes/validate and /redeem.
    /// </summary>
    public class SignupCodeRequest
    {
        #region Public-Members

        /// <summary>
        /// Code (case-insensitive).
        /// </summary>
        public string? Code { get; set; } = null;

        #endregion
    }
}
