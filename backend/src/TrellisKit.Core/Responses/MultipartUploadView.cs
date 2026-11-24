namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Multipart upload instructions.
    /// </summary>
    public class MultipartUploadView
    {
        #region Public-Members

        /// <summary>
        /// Size of every part except the last.
        /// </summary>
        public long PartSizeBytes { get; set; } = 0;

        /// <summary>
        /// Number of parts (at most 1000).
        /// </summary>
        public int PartCount { get; set; } = 0;

        /// <summary>
        /// One URL per part (bounded by partCount, at most 1000).
        /// </summary>
        public List<PresignedPart> Parts { get; set; } = new List<PresignedPart>();

        #endregion
    }
}
