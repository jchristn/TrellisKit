namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One tool call of an exported assistant reply (docs/AI_CONTRACT.md section 9.6): the tool, the arguments the model
    /// sent, the outcome, and the result links. The tool output itself is not exported (it is not stored in full and its
    /// summary may quote other members' content the reply already cites).
    /// </summary>
    public class AssistantToolCallExportItem
    {
        #region Public-Members

        /// <summary>
        /// Tool call identifier (atc_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Arguments JSON (at most 4 KB).
        /// </summary>
        public string ArgumentsJson { get; set; } = "{}";

        /// <summary>
        /// Succeeded, Failed, TimedOut, or Rejected.
        /// </summary>
        public string Status { get; set; } = "Succeeded";

        /// <summary>
        /// Results the tool found.
        /// </summary>
        public int ResultCount { get; set; } = 0;

        /// <summary>
        /// Tool round of the reply (1 based).
        /// </summary>
        public int Round { get; set; } = 1;

        /// <summary>
        /// Result links (at most 10).
        /// </summary>
        public List<ToolCallResultView> Results { get; set; } = new List<ToolCallResultView>();

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
