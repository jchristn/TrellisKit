namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// Brand images and colors. Image paths are relative to the property directory and must be under brand/.
    /// </summary>
    public class PropertyBrand
    {
        #region Public-Members

        /// <summary>
        /// Logo image path, for example brand/logo.png. Required.
        /// </summary>
        public string Logo { get; set; } = String.Empty;

        /// <summary>
        /// Square icon image path, for example brand/icon.png. Required.
        /// </summary>
        public string Icon { get; set; } = String.Empty;

        /// <summary>
        /// Favicon (.ico) path, or empty.
        /// </summary>
        public string IconIco { get; set; } = String.Empty;

        /// <summary>
        /// Brand colors.
        /// </summary>
        public PropertyBrandColors Colors { get; set; } = new PropertyBrandColors();

        #endregion
    }
}
