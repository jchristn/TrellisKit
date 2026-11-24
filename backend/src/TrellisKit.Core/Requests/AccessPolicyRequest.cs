namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// PUT /api/v1/admin/property/access body (docs/ACCESS_MODELS_CONTRACT.md section 6). Omitted fields keep their
    /// stored value; an explicit null defaultAudience returns to the model default.
    /// </summary>
    public class AccessPolicyRequest
    {
        #region Public-Members

        /// <summary>
        /// Access model, or null to keep it.
        /// </summary>
        public AccessModelEnum? Model { get; set; } = null;

        /// <summary>
        /// Anonymous browsing, or null to keep it.
        /// </summary>
        public bool? AnonymousBrowsing { get; set; } = null;

        /// <summary>
        /// Gated feature keys (replaces the list), or null to keep them. At most 20 entries.
        /// </summary>
        public List<string>? GatedFeatures { get; set; } = null;

        /// <summary>
        /// Default audience of new objects; null returns to the model default when the field is present.
        /// </summary>
        public ContentAudienceEnum? DefaultAudience { get; set; } = null;

        #endregion
    }
}
