namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A saved broadcast audience (docs/PHASE5_CONTRACT.md section 8.1).
    /// </summary>
    public class BroadcastSegment
    {
        #region Public-Members

        /// <summary>
        /// Identifier (bsg_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Creator, or null.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// Filter rows in position order (bounded by the segment filter limit).
        /// </summary>
        public List<BroadcastSegmentFilter> Filters
        {
            get { return _Filters; }
            set { _Filters = value ?? new List<BroadcastSegmentFilter>(); }
        }

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<BroadcastSegmentFilter> _Filters = new List<BroadcastSegmentFilter>();

        #endregion
    }
}
