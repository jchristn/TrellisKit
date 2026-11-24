namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// PUT /api/v1/me/appearance body: the member's palette preference (docs/TRELLISKIT.md section 2.1).
    /// </summary>
    public class AppearanceRequest
    {
        #region Public-Members

        /// <summary>
        /// An allowed engine preset id, or null (or empty) for the property's brand palette.
        /// </summary>
        public string? PalettePreset { get; set; } = null;

        #endregion
    }
}
