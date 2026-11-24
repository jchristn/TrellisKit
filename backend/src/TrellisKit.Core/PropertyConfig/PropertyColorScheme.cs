namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// Brand colors for one color scheme. Every value is a hex color such as #1F5FBF.
    /// </summary>
    public class PropertyColorScheme
    {
        #region Public-Members

        /// <summary>
        /// Primary color.
        /// </summary>
        public string Primary { get; set; } = String.Empty;

        /// <summary>
        /// Primary color on hover.
        /// </summary>
        public string PrimaryHover { get; set; } = String.Empty;

        /// <summary>
        /// Text color on the primary color.
        /// </summary>
        public string OnPrimary { get; set; } = String.Empty;

        /// <summary>
        /// Accent color.
        /// </summary>
        public string Accent { get; set; } = String.Empty;

        /// <summary>
        /// Text color on the accent color.
        /// </summary>
        public string OnAccent { get; set; } = String.Empty;

        /// <summary>
        /// Soft accent background color.
        /// </summary>
        public string AccentSoft { get; set; } = String.Empty;

        /// <summary>
        /// Text and icon color on the soft accent background (selected chips, active navigation, avatar initials).
        /// Optional: empty means primary.
        /// </summary>
        public string OnAccentSoft { get; set; } = String.Empty;

        /// <summary>
        /// Browser theme color.
        /// </summary>
        public string ThemeColor { get; set; } = String.Empty;

        #endregion
    }
}
