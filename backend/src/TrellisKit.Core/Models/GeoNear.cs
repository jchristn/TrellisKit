namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A near query: a point and a radius in kilometers.
    /// </summary>
    public class GeoNear
    {
        #region Public-Members

        /// <summary>
        /// Latitude.
        /// </summary>
        public double Lat { get; set; } = 0;

        /// <summary>
        /// Longitude.
        /// </summary>
        public double Lng { get; set; } = 0;

        /// <summary>
        /// Radius in kilometers.
        /// </summary>
        public double RadiusKm { get; set; } = 50;

        #endregion
    }
}
