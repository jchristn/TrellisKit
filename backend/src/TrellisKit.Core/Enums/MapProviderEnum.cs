namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Map renderer of a property (docs/PHASE4_CONTRACT.md section 3.6).
    /// </summary>
    public enum MapProviderEnum
    {
        /// <summary>
        /// List view only.
        /// </summary>
        None,

        /// <summary>
        /// MapLibre style on web and mobile.
        /// </summary>
        MapLibre,

        /// <summary>
        /// Platform maps on mobile; web falls back to MapLibre when a style is set.
        /// </summary>
        PlatformNative
    }
}
