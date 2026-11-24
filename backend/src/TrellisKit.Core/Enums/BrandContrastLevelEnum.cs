namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// The WCAG 2.1 AA requirement a brand color pair is checked against.
    /// </summary>
    public enum BrandContrastLevelEnum
    {
        /// <summary>
        /// Normal-size text: at least 4.5:1 (success criterion 1.4.3).
        /// </summary>
        Text,

        /// <summary>
        /// Large text (18 point, or 14 point bold): at least 3:1 (success criterion 1.4.3).
        /// </summary>
        LargeText,

        /// <summary>
        /// User interface components and graphical objects: at least 3:1 (success criterion 1.4.11).
        /// </summary>
        Component
    }
}
