namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// POST /api/v1/admin/signup-codes/{id}/revoke-all.
    /// </summary>
    public class RevokeAllResponse
    {
        #region Public-Members

        /// <summary>
        /// Redemptions revoked.
        /// </summary>
        public int RevokedCount { get; set; } = 0;

        #endregion
    }
}
