namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Messages, tokens, and cost of one member or visitor over an analytics window.
    /// </summary>
    public class AiMemberAnalyticsItem
    {
        #region Public-Members

        /// <summary>
        /// User or Visitor.
        /// </summary>
        public string SubjectType { get; set; } = String.Empty;

        /// <summary>
        /// Member or visitor id.
        /// </summary>
        public string SubjectId { get; set; } = String.Empty;

        /// <summary>
        /// Member summary (members only).
        /// </summary>
        public UserSummary? Member { get; set; } = null;

        /// <summary>
        /// Messages.
        /// </summary>
        public long Messages { get; set; } = 0;

        /// <summary>
        /// Prompt tokens.
        /// </summary>
        public long PromptTokens { get; set; } = 0;

        /// <summary>
        /// Completion tokens.
        /// </summary>
        public long CompletionTokens { get; set; } = 0;

        /// <summary>
        /// Cost in US dollars.
        /// </summary>
        public double CostUsd { get; set; } = 0;

        /// <summary>
        /// Last active day.
        /// </summary>
        public DateTime LastActiveUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
