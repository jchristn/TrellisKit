namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// POST /api/v1/admin/ranks/awards/{id}/revoke.
    /// </summary>
    public class RankRevokeRequest
    {
        #region Public-Members

        /// <summary>
        /// Reason (1 to 500 characters).
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
