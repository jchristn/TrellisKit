namespace TrellisKit.Core.Requests
{
    /// <summary>
    /// Optional body of DELETE /api/v1/venues/{id}/media/{itemId} (docs/PHASE4_CONTRACT.md section 3.13).
    /// </summary>
    public class VenueMediaRemoveRequest
    {
        #region Public-Members

        /// <summary>
        /// Why staff or an instructor removed the item (0 to 500 characters), or null.
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
