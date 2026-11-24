namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A knowledge source in an assistant body.
    /// </summary>
    public class KnowledgeSourceRequest
    {
        #region Public-Members

        /// <summary>
        /// Existing source id (kept), or null.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Content type (FeedPost in this release).
        /// </summary>
        public TargetTypeEnum? TargetType { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// All or Any.
        /// </summary>
        public LabelMatchEnum? LabelMatch { get; set; } = null;

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>
        /// 1 to 2000.
        /// </summary>
        public int? MaxItems { get; set; } = null;

        #endregion
    }
}
