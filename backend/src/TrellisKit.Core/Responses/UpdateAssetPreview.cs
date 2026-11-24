namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Bounded asset preview of an update group (at most 20).
    /// </summary>
    public class UpdateAssetPreview
    {
        /// <summary>
        /// At most 20 assets of the first update.
        /// </summary>
        public List<UpdateAssetRef> Objects { get; set; } = new List<UpdateAssetRef>();

        /// <summary>
        /// Assets of that update.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// Enumeration path, or null when the group has no assets.
        /// </summary>
        public string? Enumerate { get; set; } = null;
    }
}
