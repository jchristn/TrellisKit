namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The saved configuration of one task role: its executor hierarchy and policies (docs/INTEGRATION.md).
    /// </summary>
    public class AiTaskRoleConfig
    {
        #region Public-Members

        /// <summary>
        /// Role key (for example Extraction).
        /// </summary>
        public string Role { get; set; } = String.Empty;

        /// <summary>
        /// Reasoning effort sent with every call, or null for each endpoint's own setting.
        /// </summary>
        public ModelReasoningEffortEnum? ReasoningEffort { get; set; } = null;

        /// <summary>
        /// Behavior when an executor is busy or failing. Default FallThrough.
        /// </summary>
        public AiAvailabilityPolicyEnum AvailabilityPolicy { get; set; } = AiAvailabilityPolicyEnum.FallThrough;

        /// <summary>
        /// Behavior when every executor was tried. Default Fail.
        /// </summary>
        public AiExhaustionPolicyEnum ExhaustionPolicy { get; set; } = AiExhaustionPolicyEnum.Fail;

        /// <summary>
        /// Executors in order (at most 8).
        /// </summary>
        public List<AiExecutor> Executors { get; set; } = new List<AiExecutor>();

        /// <summary>
        /// Administrator who last changed the role, or null.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        /// <summary>
        /// UTC time of the last change, or null when the role has never been configured.
        /// </summary>
        public DateTime? UpdatedUtc { get; set; } = null;

        #endregion
    }
}
