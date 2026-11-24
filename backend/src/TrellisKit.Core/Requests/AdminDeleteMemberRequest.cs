namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of DELETE /api/v1/admin/members/{id}.
    /// </summary>
    public class AdminDeleteMemberRequest
    {
        #region Public-Members

        /// <summary>
        /// Must be DELETE.
        /// </summary>
        public string? Confirm { get; set; } = null;

        /// <summary>
        /// Why the account is deleted (required, at most 500 characters; recorded in the audit log).
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
