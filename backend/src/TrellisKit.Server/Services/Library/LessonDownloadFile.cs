namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The file a lesson download serves: the download_720p rendition when the transcoder produced it, else the original
    /// upload (docs/PHASE4_CONTRACT.md section 5.9).
    /// </summary>
    public class LessonDownloadFile
    {
        #region Public-Members

        /// <summary>
        /// Object store location (Transcoded for the rendition, VideoSource for the original).
        /// </summary>
        public MediaLocationEnum Location { get; set; } = MediaLocationEnum.Transcoded;

        /// <summary>
        /// Object store key. Never null.
        /// </summary>
        public string Key
        {
            get { return _Key; }
            set { _Key = value ?? String.Empty; }
        }

        /// <summary>
        /// File name for Content-Disposition. Never null.
        /// </summary>
        public string FileName
        {
            get { return _FileName; }
            set { _FileName = value ?? String.Empty; }
        }

        /// <summary>
        /// Content type served. Default video/mp4. Never null.
        /// </summary>
        public string ContentType
        {
            get { return _ContentType; }
            set { _ContentType = String.IsNullOrWhiteSpace(value) ? "video/mp4" : value; }
        }

        /// <summary>
        /// Size in bytes (0 when unknown).
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// SHA-256 of the file, or null when unknown (the original upload).
        /// </summary>
        public string? Sha256 { get; set; } = null;

        /// <summary>
        /// True when this is the original upload (no download rendition exists).
        /// </summary>
        public bool IsOriginal { get; set; } = false;

        #endregion

        #region Private-Members

        private string _Key = String.Empty;
        private string _FileName = String.Empty;
        private string _ContentType = "video/mp4";

        #endregion
    }
}
