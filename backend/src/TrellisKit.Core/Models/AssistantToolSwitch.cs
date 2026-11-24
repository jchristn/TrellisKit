namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// The property-wide kill switch of one assistant tool (docs/AI_CONTRACT.md section 8.3).
    /// </summary>
    public class AssistantToolSwitch
    {
        #region Public-Members

        /// <summary>
        /// Canonical tool name.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Whether the tool is disabled for every assistant of the property.
        /// </summary>
        public bool Disabled { get; set; } = false;

        /// <summary>
        /// Reason entered by the administrator.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Administrator who changed the switch last.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        /// <summary>
        /// Last change (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
