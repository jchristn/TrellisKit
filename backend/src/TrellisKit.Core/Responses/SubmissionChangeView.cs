namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// One field change of a submission (docs/PHASE4_CONTRACT.md section 2.4).
    /// </summary>
    public class SubmissionChangeView
    {
        #region Public-Members

        /// <summary>
        /// Field (for example address.line1).
        /// </summary>
        public string Field { get; set; } = String.Empty;

        /// <summary>
        /// Current value (a string, a list for multi-value fields, or null).
        /// </summary>
        public object? Current { get; set; } = null;

        /// <summary>
        /// Proposed value (a string, a list for multi-value fields, or null).
        /// </summary>
        public object? Proposed { get; set; } = null;

        /// <summary>
        /// A reviewer set the proposed value.
        /// </summary>
        public bool IsOverride { get; set; } = false;

        #endregion
    }
}
