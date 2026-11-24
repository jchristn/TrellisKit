namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of PUT /api/v1/admin/members/{id}/roles.
    /// </summary>
    public class MemberRolesRequest
    {
        #region Public-Members

        /// <summary>
        /// Replacement roles.
        /// </summary>
        public List<RoleAssignmentRequest>? Roles { get; set; } = null;

        #endregion
    }
}
