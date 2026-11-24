namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An event category.
    /// </summary>
    public class EventCategoryView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ecg_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

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
        /// Position.
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// Retired.
        /// </summary>
        public bool IsRetired { get; set; } = false;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

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
