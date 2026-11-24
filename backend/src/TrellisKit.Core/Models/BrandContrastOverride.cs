namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// An administrator's explicit acceptance of one brand color pair below its WCAG 2.1 AA minimum, stored with the
    /// palette version it was saved with and recorded as a warning in the audit trail.
    /// </summary>
    public class BrandContrastOverride
    {
        #region Public-Members

        /// <summary>
        /// Color scheme: light or dark.
        /// </summary>
        public string Scheme { get; set; } = "light";

        /// <summary>
        /// Pair identifier, foreground key then background key, for example onAccentSoft/accentSoft.
        /// </summary>
        public string Pair { get; set; } = String.Empty;

        /// <summary>
        /// Contrast ratio when the override was saved, rounded to two decimals.
        /// </summary>
        public double Ratio { get; set; } = 0;

        /// <summary>
        /// Minimum ratio the pair needs (4.5 or 3).
        /// </summary>
        public double Required { get; set; } = 0;

        /// <summary>
        /// Administrator's reason, or null. At most 200 characters.
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
