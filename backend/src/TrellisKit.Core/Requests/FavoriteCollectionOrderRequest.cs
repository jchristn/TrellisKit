namespace TrellisKit.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// Body of PUT /api/v1/me/save-collections/order: every collection of the caller exactly once, in the new order.
    /// </summary>
    public class FavoriteCollectionOrderRequest
    {
        #region Public-Members

        /// <summary>
        /// Collection identifiers (at most saves.maxCollectionsPerMember).
        /// </summary>
        public List<string>? Ids { get; set; } = null;

        #endregion
    }
}
