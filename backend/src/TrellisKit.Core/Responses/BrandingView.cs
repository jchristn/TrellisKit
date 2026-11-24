namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;

    /// <summary>
    /// GET, PUT, and POST .../revert of /api/v1/admin/property/branding (and the platform routes of the same shape): the
    /// property's current brand palette with its contrast checks, overrides, and presets (docs/TRELLISKIT.md section 2.1).
    /// </summary>
    public class BrandingView
    {
        #region Public-Members

        /// <summary>
        /// Property id.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Current palette version.
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// How the current version was created: Seed, Edit, RevertPrevious, RevertSeed, DirectorySync.
        /// </summary>
        public string Source { get; set; } = "Seed";

        /// <summary>
        /// Administrator note of the current version, or null.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Light colors (every key).
        /// </summary>
        public BrandColorSet Light { get; set; } = new BrandColorSet();

        /// <summary>
        /// Dark colors (every key).
        /// </summary>
        public BrandColorSet Dark { get; set; } = new BrandColorSet();

        /// <summary>
        /// The brand color keys, in display order. Fixed-bound: 8 keys.
        /// </summary>
        public List<string> Keys { get; set; } = new List<string>();

        /// <summary>
        /// The engine page backgrounds pairs with background are checked against.
        /// </summary>
        public BrandBackgroundsView Backgrounds { get; set; } = new BrandBackgroundsView();

        /// <summary>
        /// Every pair of both schemes with its ratio and result. Fixed-bound: 7 pairs per scheme.
        /// </summary>
        public List<BrandContrastCheck> Contrast { get; set; } = new List<BrandContrastCheck>();

        /// <summary>
        /// Contrast overrides saved with the current version. Fixed-bound: at most one per scheme and pair.
        /// </summary>
        public List<BrandContrastOverride> Overrides { get; set; } = new List<BrandContrastOverride>();

        /// <summary>
        /// The property.json seed (what a revert to the seed restores).
        /// </summary>
        public BrandPresetView Seed { get; set; } = new BrandPresetView();

        /// <summary>
        /// The version before the current one (what a revert to the previous version restores), or null.
        /// </summary>
        public BrandPresetView? Previous { get; set; } = null;

        /// <summary>
        /// The engine's palette preset catalog (docs/TRELLISKIT.md section 2.1), each with every key in both schemes.
        /// Applying one is an ordinary PUT of its colors. Fixed-bound: the catalog (at most 32 presets).
        /// </summary>
        public List<BrandPreset> Presets { get; set; } = new List<BrandPreset>();

        /// <summary>
        /// Which presets members may choose in Settings, Appearance (PUT .../branding/member-presets).
        /// </summary>
        public MemberPresetsView MemberPresets { get; set; } = new MemberPresetsView();

        /// <summary>
        /// Whether the palette follows the property folder: in SingleProperty a changed property.json brand.colors
        /// replaces the stored palette on the next start.
        /// </summary>
        public bool SyncsFromFolder { get; set; } = false;

        /// <summary>
        /// Who created the current version, or null.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// When the current version was created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
