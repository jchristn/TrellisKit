namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// POST /api/v1/admin/property/branding/revert body: restore the version before the current one (Previous), any
    /// retained version (Version, with version), or the property.json seed (Seed) as a new version.
    /// </summary>
    public class BrandingRevertRequest
    {
        #region Public-Members

        /// <summary>
        /// Previous, Seed, or Version.
        /// </summary>
        public string? To { get; set; } = null;

        /// <summary>
        /// The retained version to restore when To is Version, else ignored.
        /// </summary>
        public int? Version { get; set; } = null;

        /// <summary>
        /// Note stored with the new version, or null. At most 200 characters.
        /// </summary>
        public string? Note { get; set; } = null;

        #endregion
    }
}
