namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One text or component color checked against one background color in each color scheme (docs/TRELLISKIT.md
    /// section 2.1). The background is a brand color key or "background" (the engine page background of the scheme).
    /// </summary>
    public class BrandContrastPair
    {
        #region Public-Members

        /// <summary>
        /// Foreground brand color key, for example onPrimary.
        /// </summary>
        public string Foreground { get; set; } = String.Empty;

        /// <summary>
        /// Background brand color key, or background for the engine page background.
        /// </summary>
        public string Background { get; set; } = String.Empty;

        /// <summary>
        /// Requirement level.
        /// </summary>
        public BrandContrastLevelEnum Level { get; set; } = BrandContrastLevelEnum.Text;

        /// <summary>
        /// Where the pair appears, for administrators.
        /// </summary>
        public string Usage { get; set; } = String.Empty;

        /// <summary>
        /// Pair identifier: foreground/background, for example onPrimary/primary.
        /// </summary>
        public string Id
        {
            get { return Foreground + "/" + Background; }
        }

        /// <summary>
        /// Minimum ratio: 4.5 for normal text, 3 for large text and components.
        /// </summary>
        public double Required
        {
            get { return Level == BrandContrastLevelEnum.Text ? 4.5 : 3.0; }
        }

        #endregion
    }
}
