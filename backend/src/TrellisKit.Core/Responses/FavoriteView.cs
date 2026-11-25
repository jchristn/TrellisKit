namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A save with its target summary (REST_API.md, Saves).
    /// </summary>
    public class FavoriteView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (fav_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Target type.
        /// </summary>
        public FavoriteTargetTypeEnum TargetType { get; set; } = FavoriteTargetTypeEnum.Lesson;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Note, or null (visible to the member and to moderators).
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Collections holding the save.
        /// </summary>
        public List<string> CollectionIds { get; set; } = new List<string>();

        /// <summary>
        /// Target summary.
        /// </summary>
        public FavoriteTargetSummary Target { get; set; } = new FavoriteTargetSummary();

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update time.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
