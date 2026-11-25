namespace TrellisKit.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/me/save-collections and PUT /api/v1/me/save-collections/{id}.
    /// </summary>
    public class FavoriteCollectionRequest
    {
        #region Public-Members

        /// <summary>
        /// Name, 1 to 60 characters, unique per member (case-insensitive). Required on create; null keeps it on update.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Replacement labels, or null.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Replacement tags, or null.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
