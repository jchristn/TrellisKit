namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A member's named, private group of saves (REST_API.md, Saves). Deleting a collection keeps its saves.
    /// </summary>
    public class FavoriteCollection : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (fvc_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Owner.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Name, 1 to <see cref="MaxNameLength"/> characters, unique per member (case-insensitive).
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Position in the member's collection order (0 first).
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Saves in the collection.
        /// </summary>
        public int ItemCount { get; set; } = 0;

        /// <summary>
        /// Labels (never null).
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags (never null).
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update time.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Longest name (characters).
        /// </summary>
        public const int MaxNameLength = 60;

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
