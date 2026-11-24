namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Typed conditions of a report enumeration.
    /// </summary>
    public class ReportFilter
    {
        #region Public-Members

        /// <summary>
        /// Target type, or null.
        /// </summary>
        public TargetTypeEnum? TargetType { get; set; } = null;

        /// <summary>
        /// Target identifier, or null.
        /// </summary>
        public string? TargetId { get; set; } = null;

        /// <summary>
        /// Status, or null.
        /// </summary>
        public ReportStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Reason, or null.
        /// </summary>
        public ReportReasonEnum? Reason { get; set; } = null;

        /// <summary>
        /// Reporter, or null.
        /// </summary>
        public string? ReporterUserId { get; set; } = null;

        /// <summary>
        /// Visible scope types, or null for every scope.
        /// </summary>
        public List<string>? ScopeTypes { get; set; } = null;

        #endregion
    }
}
