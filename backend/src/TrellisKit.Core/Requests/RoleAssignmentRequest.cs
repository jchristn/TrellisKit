namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// One role in PUT /api/v1/admin/members/{id}/roles.
    /// </summary>
    public class RoleAssignmentRequest
    {
        #region Public-Members

        /// <summary>
        /// Role name (not User).
        /// </summary>
        public string? Role { get; set; } = null;

        /// <summary>
        /// Resource type (default Global).
        /// </summary>
        public string? ResourceType { get; set; } = null;

        /// <summary>
        /// Resource identifier or * (default *).
        /// </summary>
        public string? ResourceId { get; set; } = null;

        #endregion
    }
}
