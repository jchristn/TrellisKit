namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An idea status (IdeaStatus payload).
    /// </summary>
    public class IdeaStatusView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ids_).
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
        /// Hex color, or null.
        /// </summary>
        public string? Color { get; set; } = null;

        /// <summary>
        /// Whether the status is closed.
        /// </summary>
        public bool IsClosed { get; set; } = false;

        /// <summary>
        /// Whether new ideas start in this status.
        /// </summary>
        public bool IsInitial { get; set; } = false;

        /// <summary>
        /// Display order.
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// Whether retired.
        /// </summary>
        public bool IsRetired { get; set; } = false;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
