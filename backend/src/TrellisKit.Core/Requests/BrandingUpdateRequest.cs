namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// PUT /api/v1/admin/property/branding body (docs/TRELLISKIT.md section 2.1). light and dark are partial: keys that
    /// are left out keep their current value. Each value is #RGB or #RRGGBB.
    /// </summary>
    public class BrandingUpdateRequest
    {
        #region Public-Members

        /// <summary>
        /// Light colors to change, keyed by brand color key, or null.
        /// </summary>
        public Dictionary<string, string?>? Light { get; set; } = null;

        /// <summary>
        /// Dark colors to change, keyed by brand color key, or null.
        /// </summary>
        public Dictionary<string, string?>? Dark { get; set; } = null;

        /// <summary>
        /// Explicit acceptances of pairs below their WCAG 2.1 AA minimum, or null. At most 14 entries. Overrides of
        /// passing pairs are ignored.
        /// </summary>
        public List<BrandContrastOverrideRequest>? ContrastOverrides { get; set; } = null;

        /// <summary>
        /// Note stored with the new version, or null. At most 200 characters.
        /// </summary>
        public string? Note { get; set; } = null;

        #endregion
    }
}
