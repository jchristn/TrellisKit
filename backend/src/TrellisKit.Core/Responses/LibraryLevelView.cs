namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A library level (from the configured source).
    /// </summary>
    public class LibraryLevelView
    {
        #region Public-Members

        /// <summary>Custom level creation time, or creation time of the source profile field.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UnixEpoch;

        /// <summary>Custom level update time, or update time of the source profile field.</summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UnixEpoch;


        /// <summary>
        /// Key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Color, or null.
        /// </summary>
        public string? Color { get; set; } = null;

        /// <summary>
        /// Identifier (Custom levels), or null.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Sort order.
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// Retired (Custom levels).
        /// </summary>
        public bool IsRetired { get; set; } = false;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
