namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// POST /api/v1/platform/properties/{id}/admins body: annotate a member of that property as its SystemAdmin.
    /// </summary>
    public class PropertyAdminGrantRequest
    {
        #region Public-Members

        /// <summary>
        /// Member id (usr_) in that property.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        #endregion
    }
}
