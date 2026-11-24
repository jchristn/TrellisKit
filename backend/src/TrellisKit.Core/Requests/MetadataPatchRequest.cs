namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of PATCH /api/v1/metadata/{targetType}/{targetId}: incremental label and tag changes.
    /// </summary>
    public class MetadataPatchRequest
    {
        #region Public-Members

        /// <summary>
        /// Labels to add, or null.
        /// </summary>
        public List<string>? AddLabels { get; set; } = null;

        /// <summary>
        /// Labels to remove (case-insensitive), or null.
        /// </summary>
        public List<string>? RemoveLabels { get; set; } = null;

        /// <summary>
        /// Tags to set (add or overwrite), or null.
        /// </summary>
        public Dictionary<string, string>? SetTags { get; set; } = null;

        /// <summary>
        /// Tag keys to remove (case-insensitive), or null.
        /// </summary>
        public List<string>? RemoveTagKeys { get; set; } = null;

        #endregion
    }
}
