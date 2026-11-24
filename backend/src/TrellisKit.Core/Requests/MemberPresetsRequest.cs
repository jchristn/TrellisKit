namespace TrellisKit.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// PUT /api/v1/admin/property/branding/member-presets body (and the platform route of the same shape): which engine
    /// palette presets members may choose (docs/TRELLISKIT.md section 2.1).
    /// </summary>
    public class MemberPresetsRequest
    {
        #region Public-Members

        /// <summary>
        /// All, None, or Selected.
        /// </summary>
        public string? Mode { get; set; } = null;

        /// <summary>
        /// Preset ids members may choose; required (at least one) with Selected, ignored otherwise. At most the catalog
        /// size; every id must name a catalog preset.
        /// </summary>
        public List<string>? PresetIds { get; set; } = null;

        #endregion
    }
}
