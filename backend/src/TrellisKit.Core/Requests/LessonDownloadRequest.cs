namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/lessons/{id}/downloads.
    /// </summary>
    public class LessonDownloadRequest
    {
        #region Public-Members

        /// <summary>
        /// Device identifier (1 to 100 characters).
        /// </summary>
        public string? DeviceId { get; set; } = null;

        /// <summary>
        /// Ios or Android.
        /// </summary>
        public DownloadPlatformEnum? Platform { get; set; } = null;

        #endregion
    }
}
