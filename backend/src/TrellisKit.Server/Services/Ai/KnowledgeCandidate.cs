namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One retrieved knowledge item.
    /// </summary>
    public class KnowledgeCandidate
    {
        #region Public-Members

        /// <summary>
        /// Content type.
        /// </summary>
        public string TargetType { get; set; } = String.Empty;

        /// <summary>
        /// Content id.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// App path.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Plain-text excerpt.
        /// </summary>
        public string Excerpt { get; set; } = String.Empty;

        /// <summary>
        /// Retrieval score.
        /// </summary>
        public double Score { get; set; } = 0;

        /// <summary>
        /// Citation key once placed in the context.
        /// </summary>
        public int Index { get; set; } = 0;

        #endregion
    }
}
