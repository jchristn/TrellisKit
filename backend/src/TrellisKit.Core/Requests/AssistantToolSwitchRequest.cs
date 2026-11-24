namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of PUT /api/v1/admin/ai/tools/{name}: the property-wide kill switch of one tool.
    /// </summary>
    public class AssistantToolSwitchRequest
    {
        #region Public-Members

        /// <summary>
        /// Whether the tool is disabled for every assistant.
        /// </summary>
        public bool? Disabled { get; set; } = null;

        /// <summary>
        /// Reason (up to 500 characters), recorded in the audit log.
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
