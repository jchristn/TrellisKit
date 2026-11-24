namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// GET and PUT /api/v1/me/appearance: the member's palette preference and the palette that applies
    /// (docs/TRELLISKIT.md section 2.1). The allowed presets and their colors are GET /api/v1/property brand.presets.
    /// </summary>
    public class AppearanceView
    {
        #region Public-Members

        /// <summary>
        /// The stored preference: an engine preset id, or null for the property's brand palette.
        /// </summary>
        public string? PalettePreset { get; set; } = null;

        /// <summary>
        /// The preset that applies: the preference when members may still choose it, else null (the property's brand
        /// palette, for example after an administrator disallowed the preset).
        /// </summary>
        public string? EffectivePreset { get; set; } = null;

        /// <summary>
        /// Whether members may choose any preset (false when the administrator allows none).
        /// </summary>
        public bool PresetsAvailable { get; set; } = false;

        #endregion
    }
}
