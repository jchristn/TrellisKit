namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A knowledge source of an assistant: engine content selected by labels and tags.
    /// </summary>
    public class AssistantKnowledgeSource
    {
        #region Public-Members

        /// <summary>
        /// Identifier (aks_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Assistant id.
        /// </summary>
        public string AssistantId { get; set; } = String.Empty;

        /// <summary>
        /// Order.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Content type.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.FeedPost;

        /// <summary>
        /// Required labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// All or Any.
        /// </summary>
        public LabelMatchEnum LabelMatch { get; set; } = LabelMatchEnum.All;

        /// <summary>
        /// Required tags (* for any value).
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Candidate pool bound.
        /// </summary>
        public int MaxItems { get; set; } = 200;

        #endregion
    }
}
