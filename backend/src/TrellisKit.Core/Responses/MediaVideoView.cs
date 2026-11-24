namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Video fields of the Media payload.
    /// </summary>
    public class MediaVideoView
    {
        #region Public-Members

        /// <summary>
        /// HLS master playlist URL, or null.
        /// </summary>
        public string? HlsUrl { get; set; } = null;

        /// <summary>
        /// Progressive MP4 URL (Passthrough mode only), or null.
        /// </summary>
        public string? Mp4Url { get; set; } = null;

        /// <summary>
        /// Poster URL, or null.
        /// </summary>
        public string? PosterUrl { get; set; } = null;

        /// <summary>
        /// Poster thumbnail URL, or null.
        /// </summary>
        public string? PosterThumbUrl { get; set; } = null;

        #endregion
    }
}
