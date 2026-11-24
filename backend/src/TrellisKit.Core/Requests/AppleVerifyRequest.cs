namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/membership/apple/verify.
    /// </summary>
    public class AppleVerifyRequest
    {
        #region Public-Members

        /// <summary>
        /// JWS signed transaction from StoreKit 2.
        /// </summary>
        public string? SignedTransaction { get; set; } = null;

        #endregion
    }
}
