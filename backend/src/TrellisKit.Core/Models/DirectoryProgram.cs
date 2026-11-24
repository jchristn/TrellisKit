namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// An admin-defined program option a venue offers (docs/PHASE4_CONTRACT.md section 0.2).
    /// </summary>
    public class DirectoryProgram
    {
        #region Public-Members

        /// <summary>
        /// Identifier (dpg_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Stable key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Display label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Sort order.
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// Retired programs are hidden from forms and filters; stored values stay.
        /// </summary>
        public bool IsRetired { get; set; } = false;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
