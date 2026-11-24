namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Create or edit an event category.
    /// </summary>
    public class EventCategoryRequest
    {
        #region Public-Members

        /// <summary>
        /// Key (create only; letters, digits, and hyphens, 1 to 40).
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// Label (1 to 60).
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// Color as #RRGGBB, or null.
        /// </summary>
        public string? Color { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
