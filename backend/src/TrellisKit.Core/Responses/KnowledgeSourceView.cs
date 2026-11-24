namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A knowledge source.
    /// </summary>
    public class KnowledgeSourceView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Content type.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.FeedPost;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// All or Any.
        /// </summary>
        public LabelMatchEnum LabelMatch { get; set; } = LabelMatchEnum.All;

        /// <summary>
        /// Tags (* for any value).
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Candidate pool bound.
        /// </summary>
        public int MaxItems { get; set; } = 200;

        #endregion
    }
}
