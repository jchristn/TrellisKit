namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An offline download license (lessondownloads).
    /// </summary>
    public class LessonDownload
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
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Device identifier reported by the app.
        /// </summary>
        public string DeviceId { get; set; } = String.Empty;

        /// <summary>
        /// Platform.
        /// </summary>
        public DownloadPlatformEnum Platform { get; set; } = DownloadPlatformEnum.Ios;

        /// <summary>
        /// License expiry.
        /// </summary>
        public DateTime LicenseExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When revoked, or null.
        /// </summary>
        public DateTime? RevokedUtc { get; set; } = null;

        /// <summary>
        /// Why it was revoked, or null.
        /// </summary>
        public string? RevokeReason { get; set; } = null;

        /// <summary>
        /// Last renewal, or null.
        /// </summary>
        public DateTime? LastRenewedUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
