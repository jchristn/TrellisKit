namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// An admin-defined library level (librarylevels, Custom level source).
    /// </summary>
    public class LibraryLevel
    {
        #region Public-Members

        /// <summary>
        /// Identifier (llv_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Stable key (1 to 40 of letters, digits, hyphen, underscore).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Display label (1 to 40 characters).
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Color #RRGGBB, or null.
        /// </summary>
        public string? Color { get; set; } = null;

        /// <summary>
        /// Sort order (10, 20, 30, ...).
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// Whether the level is retired (kept on existing objects, not offered for new ones).
        /// </summary>
        public bool IsRetired { get; set; } = false;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last change.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
