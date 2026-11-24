namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// An option seeded from property.json (a directory program, an event category, or a library level): a stable key, a free label, and an optional color.
    /// </summary>
    public class PropertyOptionSeed
    {
        #region Public-Members

        /// <summary>
        /// Stable key (1 to 64 letters, digits, underscores, or hyphens); what the database and the API store.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Display label (1 to 80 characters).
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Optional hex color such as #1F5FBF, or null.
        /// </summary>
        public string? Color { get; set; } = null;

        #endregion
    }
}
