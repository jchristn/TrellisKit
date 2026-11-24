namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// Brand colors for the light and dark color schemes.
    /// </summary>
    public class PropertyBrandColors
    {
        #region Public-Members

        /// <summary>
        /// Light scheme colors.
        /// </summary>
        public PropertyColorScheme Light { get; set; } = new PropertyColorScheme();

        /// <summary>
        /// Dark scheme colors.
        /// </summary>
        public PropertyColorScheme Dark { get; set; } = new PropertyColorScheme();

        #endregion
    }
}
