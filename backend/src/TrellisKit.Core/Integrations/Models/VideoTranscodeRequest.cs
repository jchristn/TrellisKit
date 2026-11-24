namespace TrellisKit.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// A video to transcode: the original in the video source location and the outputs to produce.
    /// </summary>
    public class VideoTranscodeRequest
    {
        #region Public-Members

        /// <summary>
        /// Media identifier.
        /// </summary>
        public string MediaId { get; set; } = String.Empty;

        /// <summary>
        /// Property id, the first segment of every output key.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Key of the original in the video source location.
        /// </summary>
        public string SourceKey { get; set; } = String.Empty;

        /// <summary>
        /// Duration cap in seconds used to clip the input (cost backstop), or 0 for none.
        /// </summary>
        public double DurationCapSeconds { get; set; } = 0;

        /// <summary>
        /// Whether to also produce the progressive MP4 download rendition (download_720p.mp4, H.264 and AAC, at most
        /// 720p) for offline lesson downloads (docs/PHASE4_CONTRACT.md section 5.9).
        /// </summary>
        public bool DownloadRendition { get; set; } = false;

        #endregion
    }
}
