namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Knowledge preview result (docs/AI_CONTRACT.md section 5.3).
    /// </summary>
    public class KnowledgePreviewView
    {
        #region Public-Members

        /// <summary>
        /// Items placed in the context (at most maxContextItems).
        /// </summary>
        public List<KnowledgePreviewItem> Items { get; set; } = new List<KnowledgePreviewItem>();

        /// <summary>
        /// Candidates considered.
        /// </summary>
        public int TotalCandidates { get; set; } = 0;

        /// <summary>
        /// Mode used.
        /// </summary>
        public string Mode { get; set; } = String.Empty;

        #endregion
    }
}
