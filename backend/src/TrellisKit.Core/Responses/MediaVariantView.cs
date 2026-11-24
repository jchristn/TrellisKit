namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// One image variant in the Media payload (contract section 2).
    /// </summary>
    public class MediaVariantView
    {
        #region Public-Members

        /// <summary>
        /// Width in pixels.
        /// </summary>
        public int Width { get; set; } = 0;

        /// <summary>
        /// Height in pixels.
        /// </summary>
        public int Height { get; set; } = 0;

        /// <summary>
        /// WebP URL.
        /// </summary>
        public string? WebpUrl { get; set; } = null;

        /// <summary>
        /// JPEG URL.
        /// </summary>
        public string? JpegUrl { get; set; } = null;

        #endregion
    }
}
