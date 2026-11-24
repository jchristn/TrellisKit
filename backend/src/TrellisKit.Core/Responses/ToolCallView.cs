namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One tool call of a reply (docs/AI_CONTRACT.md sections 8.2 and 9.2). Arguments, the output summary, and the
    /// error are returned to administrators only.
    /// </summary>
    public class ToolCallView
    {
        #region Public-Members

        /// <summary>
        /// Tool call id.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Activity label while running, for example "Searching the library".
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Activity label when done, for example "Searched the library".
        /// </summary>
        public string CompletedLabel { get; set; } = String.Empty;

        /// <summary>
        /// Succeeded, Failed, TimedOut, or Rejected.
        /// </summary>
        public string Status { get; set; } = "Succeeded";

        /// <summary>
        /// Results returned.
        /// </summary>
        public int ResultCount { get; set; } = 0;

        /// <summary>
        /// Duration in milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Model round (1 based).
        /// </summary>
        public int Round { get; set; } = 1;

        /// <summary>
        /// Result links (at most 10).
        /// </summary>
        public List<ToolCallResultView> Results { get; set; } = new List<ToolCallResultView>();

        /// <summary>
        /// Arguments sent by the model (administrators only).
        /// </summary>
        public string? ArgumentsJson { get; set; } = null;

        /// <summary>
        /// First 500 characters of the output (administrators only).
        /// </summary>
        public string? ResultSummary { get; set; } = null;

        /// <summary>
        /// Characters of output handed to the model (administrators only).
        /// </summary>
        public int? ResultCharacters { get; set; } = null;

        /// <summary>
        /// Failure reason (administrators only).
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Creation time (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
