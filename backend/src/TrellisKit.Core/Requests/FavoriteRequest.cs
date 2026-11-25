namespace TrellisKit.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// Body of PUT /api/v1/me/saves/{targetType}/{targetId} (REST_API.md, Saves). Every field is optional; an omitted
    /// field keeps its stored value.
    /// </summary>
    public class FavoriteRequest
    {
        #region Public-Members

        /// <summary>
        /// The caller's collections that hold the save (replaces the set; at most 20; [] removes it from every
        /// collection), or null to keep the current set.
        /// </summary>
        public List<string>? CollectionIds { get; set; } = null;

        /// <summary>
        /// Note (at most 500 characters; visible to the member and to moderators). Null or empty clears it when the
        /// field is present.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Replacement labels, or null.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Replacement tags, or null.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>
        /// Most collections in one request.
        /// </summary>
        public const int MaxCollectionIds = 20;

        #endregion
    }
}
