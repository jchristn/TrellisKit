namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A frozen copy of reported messages (docs/PHASE5_CONTRACT.md section 3.9).
    /// </summary>
    public class MessageReportSnapshot
    {
        #region Public-Members

        /// <summary>
        /// Identifier (mrs_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Report identifier.
        /// </summary>
        public string ReportId { get; set; } = String.Empty;

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Reporter, or null after account deletion.
        /// </summary>
        public string? ReporterUserId { get; set; } = null;

        /// <summary>
        /// Captured (UTC).
        /// </summary>
        public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Purge time after resolution, or null.
        /// </summary>
        public DateTime? PurgeAfterUtc { get; set; } = null;

        #endregion
    }
}
