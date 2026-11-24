namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;

    /// <summary>
    /// What an assistant tool returns (docs/AI_CONTRACT.md section 8.1): bounded text for the model (at most ten
    /// results, each with its app path) and the result count for the stored tool call.
    /// </summary>
    public class AssistantToolResult
    {
        #region Public-Members

        /// <summary>
        /// Text handed to the model (the registry truncates it to <see cref="AssistantToolRegistry.MaxOutputCharacters"/>).
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Number of results (for example lessons found).
        /// </summary>
        public int ResultCount { get; set; } = 0;

        /// <summary>
        /// Outcome: Succeeded, Failed, TimedOut, or Rejected (set by the registry for failures).
        /// </summary>
        public string Status { get; set; } = "Succeeded";

        /// <summary>
        /// Result links (target, title, app path), at most 10; used for citations and the member's tool activity.
        /// </summary>
        public List<AssistantToolCallResult> Results { get; set; } = new List<AssistantToolCallResult>();

        /// <summary>
        /// Short reason for a failed, timed out, or rejected call (stored, shown to administrators).
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion
    }
}
