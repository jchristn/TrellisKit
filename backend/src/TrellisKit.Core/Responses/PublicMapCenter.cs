namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Default map center.
    /// </summary>
    public class PublicMapCenter
    {
        #region Public-Members

        /// <summary>
        /// Latitude in WGS 84 decimal degrees.
        /// </summary>
        public double Lat { get; set; } = 0;

        /// <summary>
        /// Longitude in WGS 84 decimal degrees.
        /// </summary>
        public double Lng { get; set; } = 0;

        #endregion
    }
}
