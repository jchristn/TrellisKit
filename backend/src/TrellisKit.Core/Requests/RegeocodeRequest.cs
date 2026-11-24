namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Queue geocoding of venues.
    /// </summary>
    public class RegeocodeRequest
    {
        #region Public-Members

        /// <summary>
        /// Venues (at most 500), or null for every venue.
        /// </summary>
        public List<string>? VenueIds { get; set; } = null;

        /// <summary>
        /// Only venues without a position.
        /// </summary>
        public bool OnlyMissing { get; set; } = true;

        #endregion
    }
}
