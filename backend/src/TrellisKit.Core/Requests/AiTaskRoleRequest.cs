namespace TrellisKit.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// Replace a task role's policies and executor hierarchy.
    /// </summary>
    public class AiTaskRoleRequest
    {
        #region Public-Members

        /// <summary>
        /// Minimal, Low, Medium, or High; null or empty for each endpoint's own setting.
        /// </summary>
        public string? ReasoningEffort { get; set; } = null;

        /// <summary>
        /// FallThrough or Wait, or null for FallThrough.
        /// </summary>
        public string? AvailabilityPolicy { get; set; } = null;

        /// <summary>
        /// Fail, Defer, or Wait, or null for Fail.
        /// </summary>
        public string? ExhaustionPolicy { get; set; } = null;

        /// <summary>
        /// Executors in order (at most 8); required.
        /// </summary>
        public List<AiExecutorRequest>? Executors { get; set; } = null;

        #endregion
    }
}
