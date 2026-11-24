namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An event position as the caller may see it.
    /// </summary>
    public class EventGeoView
    {
        #region Public-Members

        /// <summary>
        /// Latitude (5 places Exact, 2 Approximate, null None).
        /// </summary>
        public double? Lat { get; set; } = null;

        /// <summary>
        /// Longitude.
        /// </summary>
        public double? Lng { get; set; } = null;

        /// <summary>
        /// Exact, Approximate, or None.
        /// </summary>
        public string Precision { get; set; } = "None";

        #endregion
    }
}
