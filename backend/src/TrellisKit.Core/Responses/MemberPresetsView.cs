namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Which engine palette presets a property's members may choose (BrandingView memberPresets; docs/TRELLISKIT.md
    /// section 2.1). The property's own brand palette is always available as the default.
    /// </summary>
    public class MemberPresetsView
    {
        #region Public-Members

        /// <summary>
        /// All, None, or Selected.
        /// </summary>
        public MemberPaletteModeEnum Mode { get; set; } = MemberPaletteModeEnum.All;

        /// <summary>
        /// Allowed preset ids with Selected (catalog order), else empty. Fixed-bound: the catalog size.
        /// </summary>
        public List<string> PresetIds { get; set; } = new List<string>();

        /// <summary>
        /// Every preset id members may choose now (catalog order). Fixed-bound: the catalog size.
        /// </summary>
        public List<string> AllowedPresetIds { get; set; } = new List<string>();

        /// <summary>
        /// Revision (0 until changed).
        /// </summary>
        public int Revision { get; set; } = 0;

        /// <summary>
        /// Who changed the choices last, or null.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        /// <summary>
        /// When the choices last changed (UTC), or null.
        /// </summary>
        public DateTime? UpdatedUtc { get; set; } = null;

        #endregion
    }
}
