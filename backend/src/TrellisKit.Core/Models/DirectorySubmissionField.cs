namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One proposed value of a submission (multi-value fields are one row per value with a position).
    /// </summary>
    public class DirectorySubmissionField
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Field name (for example name, address.line1, programs).
        /// </summary>
        public string Field { get; set; } = String.Empty;

        /// <summary>
        /// Proposed value, or null to clear.
        /// </summary>
        public string? ProposedValue { get; set; } = null;

        /// <summary>
        /// Value of the target when the diff was computed, or null.
        /// </summary>
        public string? CurrentValue { get; set; } = null;

        /// <summary>
        /// Position within a multi-value field.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Whether a reviewer set this value.
        /// </summary>
        public bool IsOverride { get; set; } = false;

        #endregion
    }
}
