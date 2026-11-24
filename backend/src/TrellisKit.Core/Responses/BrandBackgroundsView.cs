namespace TrellisKit.Core.Responses
{
    using TrellisKit.Core.Helpers;

    /// <summary>
    /// The engine page background of each scheme (the "background" of a brand contrast pair).
    /// </summary>
    public class BrandBackgroundsView
    {
        #region Public-Members

        /// <summary>
        /// Light page background.
        /// </summary>
        public string Light { get; set; } = BrandContrast.LightBackground;

        /// <summary>
        /// Dark page background.
        /// </summary>
        public string Dark { get; set; } = BrandContrast.DarkBackground;

        #endregion
    }
}
