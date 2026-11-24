namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/admin/library/levels and PUT .../levels/{id}.
    /// </summary>
    public class LibraryLevelRequest
    {
        #region Public-Members

        /// <summary>
        /// Key (1 to 40 of letters, digits, hyphen, underscore; fixed after create).
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// Label (1 to 40 characters).
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// Color #RRGGBB, or null.
        /// </summary>
        public string? Color { get; set; } = null;

        /// <summary>
        /// Sort order.
        /// </summary>
        public int? SortOrder { get; set; } = null;

        /// <summary>
        /// Retired.
        /// </summary>
        public bool? IsRetired { get; set; } = null;

        #endregion
    }
}
