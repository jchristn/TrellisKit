namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Requests;

    /// <summary>
    /// A broadcast segment (docs/PHASE5_CONTRACT.md section 8.1): a saved segment, or the audience of a broadcast (then
    /// id is the saved segment it was copied from, or null for an inline segment).
    /// </summary>
    public class BroadcastSegmentView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (bsg_), or null for an inline segment.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Name, or null for an inline segment.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Filters (bounded by broadcasts filter limit).
        /// </summary>
        public List<BroadcastFilterSpec> Filters { get; set; } = new List<BroadcastFilterSpec>();

        /// <summary>
        /// Creator of a saved segment, or null.
        /// </summary>
        public UserSummary? CreatedBy { get; set; } = null;

        /// <inheritdoc />
        public List<string> Labels { get; set; } = new List<string>();

        /// <inheritdoc />
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Created, or null for an inline segment.
        /// </summary>
        public DateTime? CreatedUtc { get; set; } = null;

        /// <summary>
        /// Updated, or null for an inline segment.
        /// </summary>
        public DateTime? UpdatedUtc { get; set; } = null;

        #endregion
    }
}
