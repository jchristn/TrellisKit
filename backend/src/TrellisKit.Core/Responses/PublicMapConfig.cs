namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The effective directory map configuration without secrets (docs/PHASE4_CONTRACT.md section 3.6).
    /// </summary>
    public class PublicMapConfig
    {
        #region Public-Members

        /// <summary>
        /// None (list view only), MapLibre, or PlatformNative. Default None.
        /// </summary>
        public string Provider { get; set; } = "None";

        /// <summary>
        /// MapLibre style JSON URL (the proxied style URL when the tile proxy is on), or null.
        /// </summary>
        public string? StyleUrl { get; set; } = null;

        /// <summary>
        /// Dark mode style URL, or null.
        /// </summary>
        public string? DarkStyleUrl { get; set; } = null;

        /// <summary>
        /// Client-visible tile key restricted at the provider, or null.
        /// </summary>
        public string? PublicKey { get; set; } = null;

        /// <summary>
        /// Attribution text or Markdown link shown on the map, or null.
        /// </summary>
        public string? Attribution { get; set; } = null;

        /// <summary>
        /// Default center.
        /// </summary>
        public PublicMapCenter DefaultCenter { get; set; } = new PublicMapCenter();

        /// <summary>
        /// Default zoom (0 to 22).
        /// </summary>
        public int DefaultZoom { get; set; } = 2;

        /// <summary>
        /// Maximum zoom (0 to 22).
        /// </summary>
        public int MaxZoom { get; set; } = 18;

        /// <summary>
        /// Most map features per request.
        /// </summary>
        public int MaxFeatures { get; set; } = 500;

        #endregion
    }
}
