namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;

    /// <summary>
    /// Brand section of the public property: image paths as written in property.json, absolute URLs that serve them,
    /// and colors.
    /// </summary>
    public class PropertyBrandView
    {
        #region Public-Members

        /// <summary>
        /// Logo path relative to the property directory, for example brand/logo.png.
        /// </summary>
        public string Logo { get; set; } = String.Empty;

        /// <summary>
        /// Icon path relative to the property directory.
        /// </summary>
        public string Icon { get; set; } = String.Empty;

        /// <summary>
        /// Favicon (.ico) path relative to the property directory, or empty.
        /// </summary>
        public string IconIco { get; set; } = String.Empty;

        /// <summary>
        /// Absolute URL of the logo (GET /api/v1/property/assets/{file}).
        /// </summary>
        public string LogoUrl { get; set; } = String.Empty;

        /// <summary>
        /// Absolute URL of the icon.
        /// </summary>
        public string IconUrl { get; set; } = String.Empty;

        /// <summary>
        /// Absolute URL of the favicon, or null when the property has none.
        /// </summary>
        public string? IconIcoUrl { get; set; } = null;

        /// <summary>
        /// Brand colors: the property's effective palette (the stored palette, seeded from property.json and edited in
        /// the admin portal; docs/TRELLISKIT.md section 2.1). Every key is present in both schemes, onAccentSoft included.
        /// </summary>
        public PropertyBrandColors Colors { get; set; } = new PropertyBrandColors();

        /// <summary>
        /// Version of the effective palette (1 until an administrator edits it). Part of the response ETag.
        /// </summary>
        public int PaletteVersion { get; set; } = 1;

        /// <summary>
        /// The engine palette presets members may choose in Settings, Appearance (docs/TRELLISKIT.md section 2.1), in
        /// catalog order; empty when the administrator allows none. The brand palette above is always the default
        /// option. Fixed-bound: the preset catalog (at most 32).
        /// </summary>
        public List<BrandPreset> Presets { get; set; } = new List<BrandPreset>();

        #endregion
    }
}
