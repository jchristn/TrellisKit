namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Bounded history preview of a content object (at most 10 published revisions).
    /// </summary>
    public class ContentHistoryPreview
    {
        /// <summary>
        /// Newest published revisions.
        /// </summary>
        public List<object> Objects { get; set; } = new List<object>();

        /// <summary>
        /// Published revisions.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// Enumeration path.
        /// </summary>
        public string Enumerate { get; set; } = String.Empty;
    }
}
