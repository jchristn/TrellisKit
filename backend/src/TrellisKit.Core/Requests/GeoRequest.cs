namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// A position in a request: a manual pin.
    /// </summary>
    public class GeoRequest
    {
        #region Public-Members

        /// <summary>
        /// Latitude, -90 to 90.
        /// </summary>
        public double? Lat { get; set; } = null;

        /// <summary>
        /// Longitude, -180 to 180.
        /// </summary>
        public double? Lng { get; set; } = null;

        #endregion
    }
}
