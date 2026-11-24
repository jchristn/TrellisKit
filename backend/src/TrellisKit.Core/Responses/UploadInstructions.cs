namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// How the client uploads the bytes (contract section 4.2).
    /// </summary>
    public class UploadInstructions
    {
        #region Public-Members

        /// <summary>
        /// Single or Multipart.
        /// </summary>
        public UploadModeEnum Mode { get; set; } = UploadModeEnum.Single;

        /// <summary>
        /// When the URLs expire.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Single PUT, or null for multipart.
        /// </summary>
        public PresignedUpload? Single { get; set; } = null;

        /// <summary>
        /// Multipart instructions, or null for single.
        /// </summary>
        public MultipartUploadView? Multipart { get; set; } = null;

        /// <summary>
        /// Parts storage already has (bounded by partCount).
        /// </summary>
        public List<UploadedPartView> UploadedParts { get; set; } = new List<UploadedPartView>();

        #endregion
    }
}
