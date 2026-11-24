namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of PUT /api/v1/admin/app/kill-switches/{featureKey}.
    /// </summary>
    public class KillSwitchRequest
    {
        /// <summary>
        /// Kill (true) or restore (false). Killed without rules kills for everyone.
        /// </summary>
        public bool Killed { get; set; } = false;

        /// <summary>
        /// Message shown to members (up to 200 characters).
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Targeting rules: matching callers are killed.
        /// </summary>
        public List<FlagRuleRequest>? Rules { get; set; } = null;

        /// <summary>
        /// Whether the API blocks the feature's routes, or null to keep.
        /// </summary>
        public bool? BlocksServer { get; set; } = null;

        /// <summary>
        /// Reason for the audit trail (1 to 500 characters).
        /// </summary>
        public string Reason { get; set; } = String.Empty;
    }
}
