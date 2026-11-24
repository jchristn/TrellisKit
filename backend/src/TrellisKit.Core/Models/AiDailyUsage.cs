namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Daily messages, tokens, and cost of one member or visitor with one assistant (allowances and cost by member).
    /// </summary>
    public class AiDailyUsage
    {
        #region Public-Members

        /// <summary>
        /// Assistant id.
        /// </summary>
        public string AssistantId { get; set; } = String.Empty;

        /// <summary>
        /// User or Visitor.
        /// </summary>
        public string SubjectType { get; set; } = String.Empty;

        /// <summary>
        /// Member or visitor id.
        /// </summary>
        public string SubjectId { get; set; } = String.Empty;

        /// <summary>
        /// UTC date, yyyy-MM-dd.
        /// </summary>
        public string Date { get; set; } = String.Empty;

        /// <summary>
        /// Member messages sent.
        /// </summary>
        public int Messages { get; set; } = 0;

        /// <summary>
        /// Prompt tokens.
        /// </summary>
        public long PromptTokens { get; set; } = 0;

        /// <summary>
        /// Completion tokens.
        /// </summary>
        public long CompletionTokens { get; set; } = 0;

        /// <summary>
        /// Cost in millionths of a US dollar.
        /// </summary>
        public long CostMicros { get; set; } = 0;

        #endregion
    }
}
