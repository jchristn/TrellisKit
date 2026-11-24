namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/admin/metadata/bulk: one incremental change applied to up to 500 objects of one type.
    /// </summary>
    public class MetadataBulkRequest
    {
        #region Public-Members

        /// <summary>
        /// Object type name, for example FeedPost.
        /// </summary>
        public string? TargetType { get; set; } = null;

        /// <summary>
        /// Object identifiers (1 to 500).
        /// </summary>
        public List<string>? TargetIds { get; set; } = null;

        /// <summary>
        /// Labels to add, or null.
        /// </summary>
        public List<string>? AddLabels { get; set; } = null;

        /// <summary>
        /// Labels to remove, or null.
        /// </summary>
        public List<string>? RemoveLabels { get; set; } = null;

        /// <summary>
        /// Tags to set, or null.
        /// </summary>
        public Dictionary<string, string>? SetTags { get; set; } = null;

        /// <summary>
        /// Tag keys to remove, or null.
        /// </summary>
        public List<string>? RemoveTagKeys { get; set; } = null;

        #endregion
    }
}
