namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// One saved item of a member (REST_API.md, Saves). A member saves a target at most once; the optional note is
    /// visible to the member and to moderators (it passes the word filter and the content changelog).
    /// </summary>
    public class Favorite : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (fav_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member who saved the target.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Target type.
        /// </summary>
        public FavoriteTargetTypeEnum TargetType { get; set; } = FavoriteTargetTypeEnum.Lesson;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Note, at most <see cref="MaxNoteLength"/> characters, or null.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Collections holding this save, in collection order (at most saves.maxCollectionsPerMember).
        /// </summary>
        public List<string> CollectionIds
        {
            get { return _CollectionIds; }
            set { _CollectionIds = value ?? new List<string>(); }
        }

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
        /// Longest note (characters).
        /// </summary>
        public const int MaxNoteLength = 500;

        #endregion

        #region Private-Members

        private List<string> _CollectionIds = new List<string>();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
