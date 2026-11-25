namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Membership of one save in one collection. Both rows always belong to the same member and property; the
    /// database methods refuse any other link.
    /// </summary>
    public class FavoriteCollectionItem
    {
        #region Public-Members

        /// <summary>
        /// Collection identifier (fvc_).
        /// </summary>
        public string CollectionId { get; set; } = String.Empty;

        /// <summary>
        /// Save identifier (fav_).
        /// </summary>
        public string FavoriteId { get; set; } = String.Empty;

        /// <summary>
        /// Position in the collection (0 first).
        /// </summary>
        public int Position { get; set; } = 0;

        #endregion
    }
}
