namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of PUT /api/v1/metadata/{targetType}/{targetId}: a provided list or dictionary replaces the stored one; omitted means unchanged.
    /// </summary>
    public class MetadataReplaceRequest
    {
        #region Public-Members

        /// <summary>
        /// Replacement labels, or null to leave them.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Replacement tags, or null to leave them.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
