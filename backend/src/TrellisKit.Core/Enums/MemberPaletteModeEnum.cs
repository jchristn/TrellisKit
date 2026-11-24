namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Which engine palette presets members may choose in Settings, Appearance (docs/TRELLISKIT.md section 2.1). The
    /// property's own brand palette is always available to members as the default option.
    /// </summary>
    public enum MemberPaletteModeEnum
    {
        /// <summary>
        /// Every preset in the engine catalog.
        /// </summary>
        All,

        /// <summary>
        /// No preset: members always see the property's brand palette.
        /// </summary>
        None,

        /// <summary>
        /// Only the presets listed in presetIds.
        /// </summary>
        Selected
    }
}
