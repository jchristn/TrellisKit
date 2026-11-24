namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A latitude and longitude box; West greater than East means the box crosses the antimeridian.
    /// </summary>
    public class GeoBox
    {
        #region Public-Members

        /// <summary>
        /// Southern latitude.
        /// </summary>
        public double South { get; set; } = 0;

        /// <summary>
        /// Western longitude.
        /// </summary>
        public double West { get; set; } = 0;

        /// <summary>
        /// Northern latitude.
        /// </summary>
        public double North { get; set; } = 0;

        /// <summary>
        /// Eastern longitude.
        /// </summary>
        public double East { get; set; } = 0;

        #endregion
    }
}
