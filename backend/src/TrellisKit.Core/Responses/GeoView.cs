namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A position as the caller may see it (docs/PHASE4_CONTRACT.md section 2.1).
    /// </summary>
    public class GeoView
    {
        #region Public-Members

        /// <summary>
        /// Latitude (5 decimals Exact, 2 Approximate, null None).
        /// </summary>
        public double? Lat { get; set; } = null;

        /// <summary>
        /// Longitude (5 decimals Exact, 2 Approximate, null None).
        /// </summary>
        public double? Lng { get; set; } = null;

        /// <summary>
        /// What the caller receives.
        /// </summary>
        public GeoPrecisionEnum Precision { get; set; } = GeoPrecisionEnum.None;

        /// <summary>
        /// Where the position came from (staff only; null for members).
        /// </summary>
        public GeoSourceEnum? Source { get; set; } = null;

        /// <summary>
        /// When it was set (staff only).
        /// </summary>
        public DateTime? GeocodedUtc { get; set; } = null;

        #endregion
    }
}
