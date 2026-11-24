namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An asynchronous account export job (table accountexports).
    /// </summary>
    public class AccountExport
    {
        #region Public-Members

        /// <summary>
        /// Identifier (aex_ prefix).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member the export belongs to.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public AccountExportStatusEnum Status { get; set; } = AccountExportStatusEnum.Pending;

        /// <summary>
        /// When the export was requested (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the worker started writing (UTC), or null.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// When the file was finished or the job failed (UTC), or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// When the file is deleted (UTC), or null until ready.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// File size in bytes, or null until ready.
        /// </summary>
        public long? SizeBytes { get; set; } = null;

        /// <summary>
        /// Failure description (never shown to members), or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Worker instance holding the claim, or null.
        /// </summary>
        public string? ClaimedBy { get; set; } = null;

        /// <summary>
        /// When the claim lapses (UTC), or null.
        /// </summary>
        public DateTime? ClaimUntilUtc { get; set; } = null;

        #endregion
    }
}
