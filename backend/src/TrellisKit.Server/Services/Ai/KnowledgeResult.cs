namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of knowledge retrieval for one message.
    /// </summary>
    public class KnowledgeResult
    {
        #region Public-Members

        /// <summary>
        /// Items placed in the context.
        /// </summary>
        public List<KnowledgeCandidate> Items { get; set; } = new List<KnowledgeCandidate>();

        /// <summary>
        /// Candidates considered.
        /// </summary>
        public int TotalCandidates { get; set; } = 0;

        /// <summary>
        /// keyword, vector, or hybrid.
        /// </summary>
        public string Mode { get; set; } = "keyword";

        /// <summary>
        /// System message with the numbered items, or null when none.
        /// </summary>
        public string? ContextBlock { get; set; } = null;

        /// <summary>
        /// Query used (after rewriting).
        /// </summary>
        public string Query { get; set; } = String.Empty;

        #endregion
    }
}
