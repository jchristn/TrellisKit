namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a brand palette version was created (docs/TRELLISKIT.md section 2.1).
    /// </summary>
    public enum BrandPaletteSourceEnum
    {
        /// <summary>
        /// Seeded from property.json brand.colors on the property's first load.
        /// </summary>
        Seed,

        /// <summary>
        /// Edited by an administrator.
        /// </summary>
        Edit,

        /// <summary>
        /// An administrator restored the version before the current one.
        /// </summary>
        RevertPrevious,

        /// <summary>
        /// An administrator restored the property.json seed.
        /// </summary>
        RevertSeed,

        /// <summary>
        /// An administrator restored a specific retained version (revert to Version).
        /// </summary>
        RevertVersion,

        /// <summary>
        /// The property folder's brand.colors changed on disk and replaced the stored palette (SingleProperty, the
        /// folder wins on change).
        /// </summary>
        DirectorySync
    }
}
