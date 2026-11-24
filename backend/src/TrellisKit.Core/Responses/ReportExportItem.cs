namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Account export item of a report the member filed.
    /// </summary>
    public class ReportExportItem
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Target type.
        /// </summary>
        public string TargetType { get; set; } = String.Empty;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Reason.
        /// </summary>
        public string Reason { get; set; } = String.Empty;

        /// <summary>
        /// Details.
        /// </summary>
        public string? Details { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public string Status { get; set; } = String.Empty;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Resolved (UTC).
        /// </summary>
        public DateTime? ResolvedUtc { get; set; } = null;

        #endregion
    }
}
