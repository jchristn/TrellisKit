namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One retrieved knowledge item.
    /// </summary>
    public class KnowledgePreviewItem : CatalogEntryMetadata
    {
        #region Public-Members

        /// <summary>Stable compound identity of the retrieved target.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Zero-based relevance rank in the bounded context shortlist.</summary>
        public int Rank { get; set; }


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
        /// Score.
        /// </summary>
        public double? Score { get; set; } = null;

        /// <summary>
        /// Excerpt.
        /// </summary>
        public string Excerpt { get; set; } = String.Empty;

        #endregion
    }
}
