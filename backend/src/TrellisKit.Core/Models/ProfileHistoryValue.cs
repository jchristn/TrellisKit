namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One sub-field value of a History entry.
    /// </summary>
    public class ProfileHistoryValue
    {
        #region Public-Members

        /// <summary>
        /// Sub-field key.
        /// </summary>
        public string SubFieldKey { get; set; } = String.Empty;

        /// <summary>
        /// Text, URL, option key, rank key, or member name.
        /// </summary>
        public string? ValueText { get; set; } = null;

        /// <summary>
        /// Normalized text.
        /// </summary>
        public string? ValueNormalized { get; set; } = null;

        /// <summary>
        /// Number.
        /// </summary>
        public double? ValueNumber { get; set; } = null;

        /// <summary>
        /// Date (YYYY-MM-DD).
        /// </summary>
        public string? ValueDate { get; set; } = null;

        /// <summary>
        /// Boolean.
        /// </summary>
        public bool? ValueBool { get; set; } = null;

        /// <summary>
        /// Rank level.
        /// </summary>
        public int? ValueLevel { get; set; } = null;

        /// <summary>
        /// Member sub-field: referenced member id.
        /// </summary>
        public string? ValueUserId { get; set; } = null;

        #endregion
    }
}
