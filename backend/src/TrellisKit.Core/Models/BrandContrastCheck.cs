namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The contrast of one brand color pair in one scheme (GET /api/v1/admin/property/branding contrast).
    /// </summary>
    public class BrandContrastCheck
    {
        #region Public-Members

        /// <summary>
        /// Color scheme: light or dark.
        /// </summary>
        public string Scheme { get; set; } = "light";

        /// <summary>
        /// Pair identifier, for example onPrimary/primary.
        /// </summary>
        public string Pair { get; set; } = String.Empty;

        /// <summary>
        /// Foreground key.
        /// </summary>
        public string Foreground { get; set; } = String.Empty;

        /// <summary>
        /// Background key (a brand color key or background).
        /// </summary>
        public string Background { get; set; } = String.Empty;

        /// <summary>
        /// Foreground color (#RRGGBB).
        /// </summary>
        public string ForegroundColor { get; set; } = String.Empty;

        /// <summary>
        /// Background color (#RRGGBB).
        /// </summary>
        public string BackgroundColor { get; set; } = String.Empty;

        /// <summary>
        /// Contrast ratio rounded to two decimals (never rounded up to a threshold it misses).
        /// </summary>
        public double Ratio { get; set; } = 0;

        /// <summary>
        /// Minimum ratio (4.5 or 3).
        /// </summary>
        public double Required { get; set; } = 0;

        /// <summary>
        /// Requirement level.
        /// </summary>
        public BrandContrastLevelEnum Level { get; set; } = BrandContrastLevelEnum.Text;

        /// <summary>
        /// Whether the ratio meets the minimum.
        /// </summary>
        public bool Passes { get; set; } = false;

        /// <summary>
        /// Whether a failing pair was saved with an explicit administrator override.
        /// </summary>
        public bool Overridden { get; set; } = false;

        /// <summary>
        /// Where the pair appears.
        /// </summary>
        public string Usage { get; set; } = String.Empty;

        #endregion
    }
}
