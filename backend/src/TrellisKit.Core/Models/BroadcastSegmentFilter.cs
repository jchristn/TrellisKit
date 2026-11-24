namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One stored segment filter value (table broadcastsegmentfilters; never JSON). A filter with several values (for
    /// example profile.rank in two keys) is several rows with the same position, field, and operator.
    /// </summary>
    public class BroadcastSegmentFilter
    {
        #region Public-Members

        /// <summary>
        /// Row identifier (bsf_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Field, for example membership, profile.rank, labels, or tags.level.
        /// </summary>
        public string Field { get; set; } = String.Empty;

        /// <summary>
        /// Operator, for example eq, in, any, or all.
        /// </summary>
        public string Operator { get; set; } = String.Empty;

        /// <summary>
        /// One value as text (booleans true or false, numbers in invariant culture, timestamps ISO 8601 UTC).
        /// </summary>
        public string Value { get; set; } = String.Empty;

        /// <summary>
        /// Kind of the value as sent: String, Number, Boolean, or List (a list value is one row per item).
        /// </summary>
        public string Kind { get; set; } = "String";

        /// <summary>
        /// Filter position (zero based) in the segment.
        /// </summary>
        public int Position { get; set; } = 0;

        #endregion
    }
}
