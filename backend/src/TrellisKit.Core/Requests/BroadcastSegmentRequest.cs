namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A broadcast segment: the body of POST and PUT /api/v1/admin/broadcasts/segments, and the inline segment of a
    /// broadcast or a preview (where name and description are optional).
    /// </summary>
    public class BroadcastSegmentRequest
    {
        #region Public-Members

        /// <summary>
        /// Name (1 to 100 characters; required for saved segments).
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description (at most 500 characters), or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Filters combined with AND (at most broadcasts filter limit), or null for everyone the access model allows.
        /// </summary>
        public List<BroadcastFilterSpec>? Filters { get; set; } = null;

        /// <summary>
        /// Labels of a saved segment.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags of a saved segment.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
