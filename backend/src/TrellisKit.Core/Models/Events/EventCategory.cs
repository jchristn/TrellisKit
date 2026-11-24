namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An event category (docs/PHASE4_CONTRACT.md section 0.2).
    /// </summary>
    public class EventCategory
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ecg_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Stable key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Color as #RRGGBB, or null.
        /// </summary>
        public string? Color { get; set; } = null;

        /// <summary>
        /// Position.
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// Retired categories stay on existing events but cannot be chosen.
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
