namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/membership/google/verify.
    /// </summary>
    public class GoogleVerifyRequest
    {
        #region Public-Members

        /// <summary>
        /// Play Billing purchase token.
        /// </summary>
        public string? PurchaseToken { get; set; } = null;

        /// <summary>
        /// Subscription product identifier.
        /// </summary>
        public string? ProductId { get; set; } = null;

        #endregion
    }
}
