namespace TrellisKit.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// Outcome of a transcode. Synchronous processors return the outputs; asynchronous ones return only a job id.
    /// </summary>
    public class VideoTranscodeResult
    {
        #region Public-Members

        /// <summary>
        /// Whether the outputs are written (false while an asynchronous job runs).
        /// </summary>
        public bool IsComplete { get; set; } = true;

        /// <summary>
        /// Asynchronous job identifier, or null.
        /// </summary>
        public string? JobId { get; set; } = null;

        /// <summary>
        /// HLS master playlist file name under video/{id}/ in the transcoded location, or null.
        /// </summary>
        public string? HlsManifestFile { get; set; } = null;

        /// <summary>
        /// Progressive MP4 file name under video/{id}/ in the transcoded location, or null.
        /// </summary>
        public string? Mp4File { get; set; } = null;

        /// <summary>
        /// Poster file name under media/{id}/ in the images location, or null.
        /// </summary>
        public string? PosterFile { get; set; } = null;

        /// <summary>
        /// Poster thumbnail file name under media/{id}/ in the images location, or null.
        /// </summary>
        public string? PosterThumbFile { get; set; } = null;

        /// <summary>
        /// Display width, or null when unknown.
        /// </summary>
        public int? Width { get; set; } = null;

        /// <summary>
        /// Display height, or null when unknown.
        /// </summary>
        public int? Height { get; set; } = null;

        /// <summary>
        /// Duration in seconds, or null when unknown.
        /// </summary>
        public double? DurationSeconds { get; set; } = null;

        /// <summary>
        /// File name of the download rendition in the transcoded location, or null.
        /// </summary>
        public string? DownloadFile { get; set; } = null;

        /// <summary>
        /// Size of the download rendition in bytes (0 when unknown).
        /// </summary>
        public long DownloadSizeBytes { get; set; } = 0;

        /// <summary>
        /// SHA-256 of the download rendition (hex, lowercase), or null when unknown.
        /// </summary>
        public string? DownloadSha256 { get; set; } = null;

        #endregion
    }
}
