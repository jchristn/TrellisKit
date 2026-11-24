namespace TrellisKit.Core.Models
{
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Filters of the AI call ledger.
    /// </summary>
    public class AiCallFilter
    {
        #region Public-Members

        /// <summary>
        /// Task roles (empty for any).
        /// </summary>
        public List<string> Roles { get; set; } = new List<string>();

        /// <summary>
        /// Purposes (empty for any).
        /// </summary>
        public List<AssistantPurposeEnum> Purposes { get; set; } = new List<AssistantPurposeEnum>();

        /// <summary>
        /// Outcomes (empty for any).
        /// </summary>
        public List<AiUsageOutcomeEnum> Outcomes { get; set; } = new List<AiUsageOutcomeEnum>();

        /// <summary>
        /// Endpoint, or null.
        /// </summary>
        public string? EndpointId { get; set; } = null;

        /// <summary>
        /// Job key, or null.
        /// </summary>
        public string? JobKey { get; set; } = null;

        /// <summary>
        /// Job run, or null.
        /// </summary>
        public string? JobRunId { get; set; } = null;

        /// <summary>
        /// Assistant, or null.
        /// </summary>
        public string? AssistantId { get; set; } = null;

        #endregion
    }
}
