namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Models;

    /// <summary>
    /// A palette the Branding page offers as a preset: the property.json seed, or the version before the current one.
    /// </summary>
    public class BrandPresetView
    {
        #region Public-Members

        /// <summary>
        /// Palette version, or null for the property.json seed.
        /// </summary>
        public int? Version { get; set; } = null;

        /// <summary>
        /// How the version was created, or null for the seed.
        /// </summary>
        public string? Source { get; set; } = null;

        /// <summary>
        /// When the version was created (UTC), or null for the seed.
        /// </summary>
        public DateTime? CreatedUtc { get; set; } = null;

        /// <summary>
        /// Light colors.
        /// </summary>
        public BrandColorSet Light { get; set; } = new BrandColorSet();

        /// <summary>
        /// Dark colors.
        /// </summary>
        public BrandColorSet Dark { get; set; } = new BrandColorSet();

        #endregion
    }
}
