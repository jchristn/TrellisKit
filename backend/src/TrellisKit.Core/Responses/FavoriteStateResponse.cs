namespace TrellisKit.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Response of POST /api/v1/me/saves/state: one item per requested target, in request order (at most 100, the
    /// request bound).
    /// </summary>
    public class FavoriteStateResponse
    {
        #region Public-Members

        /// <summary>
        /// Items.
        /// </summary>
        public List<FavoriteStateItem> Items { get; set; } = new List<FavoriteStateItem>();

        #endregion
    }
}
