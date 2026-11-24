namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// GET /api/v1/me/export/{id}: an export job and, when Ready, a short-lived signed download URL.
    /// </summary>
    public class AccountExportView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public AccountExportStatusEnum Status { get; set; } = AccountExportStatusEnum.Pending;

        /// <summary>
        /// When the export was requested (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the file was finished or generation failed (UTC), or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// When the file is deleted (UTC), or null until Ready.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// Signed download URL (relative to the API), valid for a few minutes, or null unless Ready.
        /// </summary>
        public string? DownloadUrl { get; set; } = null;

        /// <summary>
        /// File size in bytes, or null until Ready.
        /// </summary>
        public long? SizeBytes { get; set; } = null;

        /// <summary>
        /// Labels (export jobs carry none; always empty).
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags (export jobs carry none; always empty).
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
