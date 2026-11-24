namespace TrellisKit.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Response of POST /api/v1/venues/{id}/media: the new items in request order (at most 10, fixed bound).
    /// </summary>
    public class VenueMediaAddResponse
    {
        #region Public-Members

        /// <summary>
        /// New items.
        /// </summary>
        public List<VenueMediaItemView> Objects { get; set; } = new List<VenueMediaItemView>();

        #endregion
    }
}
