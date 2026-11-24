namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Audit record list filter.
    /// </summary>
    public class AuditFilter
    {
        #region Public-Members

        /// <summary>
        /// Actor, or null.
        /// </summary>
        public string? ActorUserId { get; set; } = null;

        /// <summary>
        /// Target type, or null.
        /// </summary>
        public string? TargetType { get; set; } = null;

        /// <summary>
        /// Target identifier, or null.
        /// </summary>
        public string? TargetId { get; set; } = null;

        /// <summary>
        /// Action, or null.
        /// </summary>
        public string? Action { get; set; } = null;

        /// <summary>
        /// Inclusive start (UTC), or null.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Exclusive end (UTC), or null.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        #endregion
    }
}
