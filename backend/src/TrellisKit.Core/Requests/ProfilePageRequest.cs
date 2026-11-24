namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of PUT /api/v1/me/public-page and PUT /api/v1/admin/members/{id}/public-page (omitted fields stay).
    /// </summary>
    public class ProfilePageRequest
    {
        #region Public-Members

        /// <summary>
        /// Whether the public profile page is on.
        /// </summary>
        public bool? Enabled { get; set; } = null;

        /// <summary>
        /// New slug (3 to 80 lower case letters, digits, and single hyphens), or null to keep the current one (or use the
        /// suggested one when the page is turned on without a slug).
        /// </summary>
        public string? Slug { get; set; } = null;

        #endregion
    }
}
