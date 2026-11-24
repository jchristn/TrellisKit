namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A manual position for an event location.
    /// </summary>
    public class EventGeoRequest
    {
        #region Public-Members

        /// <summary>
        /// Latitude (-90 to 90).
        /// </summary>
        public double? Lat { get; set; } = null;

        /// <summary>
        /// Longitude (-180 to 180).
        /// </summary>
        public double? Lng { get; set; } = null;

        #endregion
    }
}
