namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One engine palette preset (docs/TRELLISKIT.md section 2.1): a neutral, named palette with every brand color key
    /// in both schemes. The catalog is the engine asset Assets/palette-presets.json; every preset passes every WCAG 2.1
    /// AA pair in both schemes without an override.
    /// Thread safety: not thread safe; catalog entries are cloned before they are handed out.
    /// </summary>
    public class BrandPreset
    {
        #region Public-Members

        /// <summary>
        /// Stable id (lowercase letters, digits, and hyphens), for example ocean.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Display name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// One-sentence description.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Light colors (every key).
        /// </summary>
        public BrandColorSet Light { get; set; } = new BrandColorSet();

        /// <summary>
        /// Dark colors (every key).
        /// </summary>
        public BrandColorSet Dark { get; set; } = new BrandColorSet();

        #endregion

        #region Public-Methods

        /// <summary>
        /// A copy of this preset.
        /// </summary>
        /// <returns>Copy.</returns>
        public BrandPreset Clone()
        {
            return new BrandPreset { Id = Id, Name = Name, Description = Description, Light = Light.Clone(), Dark = Dark.Clone() };
        }

        #endregion
    }
}
