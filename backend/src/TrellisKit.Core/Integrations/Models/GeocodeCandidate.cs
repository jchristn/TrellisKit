namespace TrellisKit.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// One geocoder candidate.
    /// </summary>
    public class GeocodeCandidate
    {
        #region Public-Members

        /// <summary>
        /// Display label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Latitude (WGS 84).
        /// </summary>
        public double Lat { get; set; } = 0;

        /// <summary>
        /// Longitude (WGS 84).
        /// </summary>
        public double Lng { get; set; } = 0;

        /// <summary>
        /// Southern extent, or null.
        /// </summary>
        public double? South { get; set; } = null;

        /// <summary>
        /// Western extent, or null.
        /// </summary>
        public double? West { get; set; } = null;

        /// <summary>
        /// Northern extent, or null.
        /// </summary>
        public double? North { get; set; } = null;

        /// <summary>
        /// Eastern extent, or null.
        /// </summary>
        public double? East { get; set; } = null;

        /// <summary>
        /// IANA time zone, or null when the provider does not return one.
        /// </summary>
        public string? TimeZone { get; set; } = null;

        #endregion
    }
}
