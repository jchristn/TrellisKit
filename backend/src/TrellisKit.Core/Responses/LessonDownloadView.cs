namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An offline download license (docs/PHASE4_CONTRACT.md section 5.9).
    /// </summary>
    public class LessonDownloadView
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ldl_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Lesson.
        /// </summary>
        public string LessonId { get; set; } = String.Empty;

        /// <summary>
        /// Device.
        /// </summary>
        public string DeviceId { get; set; } = String.Empty;

        /// <summary>
        /// Platform.
        /// </summary>
        public DownloadPlatformEnum Platform { get; set; } = DownloadPlatformEnum.Ios;

        /// <summary>
        /// Short-lived signed URL of the rendition (issue and renew), or null.
        /// </summary>
        public string? Url { get; set; } = null;

        /// <summary>
        /// When the URL expires, or null.
        /// </summary>
        public DateTime? UrlExpiresUtc { get; set; } = null;

        /// <summary>
        /// License expiry.
        /// </summary>
        public DateTime LicenseExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Rendition size.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// SHA-256 of the rendition (hex), or null.
        /// </summary>
        public string? Sha256 { get; set; } = null;

        /// <summary>
        /// When revoked.
        /// </summary>
        public DateTime? RevokedUtc { get; set; } = null;

        /// <summary>
        /// Why revoked.
        /// </summary>
        public string? RevokeReason { get; set; } = null;

        /// <summary>
        /// Last renewal.
        /// </summary>
        public DateTime? LastRenewedUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
