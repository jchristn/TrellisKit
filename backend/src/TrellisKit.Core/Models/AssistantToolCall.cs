namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One tool call of an assistant reply (docs/AI_CONTRACT.md section 8.2). Tool outputs are not stored in full.
    /// </summary>
    public class AssistantToolCall
    {
        #region Public-Members

        /// <summary>
        /// Tool call id (atc_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Reply message id.
        /// </summary>
        public string MessageId { get; set; } = String.Empty;

        /// <summary>
        /// Order within the reply (0 based).
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Model round that requested the call (1 based).
        /// </summary>
        public int Round { get; set; } = 1;

        /// <summary>
        /// Canonical tool name (or the unknown name the model asked for).
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Arguments as sent by the model, bounded to 4 KB (opaque).
        /// </summary>
        public string ArgumentsJson { get; set; } = "{}";

        /// <summary>
        /// Succeeded, Failed, TimedOut, or Rejected.
        /// </summary>
        public string Status { get; set; } = "Succeeded";

        /// <summary>
        /// First 500 characters of the output handed to the model.
        /// </summary>
        public string? ResultSummary { get; set; } = null;

        /// <summary>
        /// Results returned.
        /// </summary>
        public int ResultCount { get; set; } = 0;

        /// <summary>
        /// Characters of output handed to the model (after truncation).
        /// </summary>
        public int ResultCharacters { get; set; } = 0;

        /// <summary>
        /// Duration in milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Short reason for a failed, timed out, or rejected call.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Result links (at most 10).
        /// </summary>
        public List<AssistantToolCallResult> Results { get; set; } = new List<AssistantToolCallResult>();

        /// <summary>
        /// Creation time (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
