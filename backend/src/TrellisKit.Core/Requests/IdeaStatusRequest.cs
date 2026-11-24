namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Create or edit an idea status (POST and PUT /api/v1/admin/ideas/statuses). Omitted fields stay unchanged on edits.
    /// </summary>
    public class IdeaStatusRequest
    {
        #region Public-Members

        /// <summary>
        /// Key (1 to 64 letters, digits, underscores, or hyphens; required on create).
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// Label (1 to 60 characters; required on create).
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// Hex color, or null.
        /// </summary>
        public string? Color { get; set; } = null;

        /// <summary>
        /// Whether the status is closed.
        /// </summary>
        public bool? IsClosed { get; set; } = null;

        /// <summary>
        /// Whether new ideas start in this status (clears the flag on the others).
        /// </summary>
        public bool? IsInitial { get; set; } = null;

        #endregion
    }
}
