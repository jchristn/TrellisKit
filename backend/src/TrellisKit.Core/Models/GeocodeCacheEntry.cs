namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A cached geocoder answer (docs/PHASE4_CONTRACT.md section 3.5).
    /// </summary>
    public class GeocodeCacheEntry
    {
        #region Public-Members

        /// <summary>
        /// Identifier (gcr_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// SHA-256 of the normalized query.
        /// </summary>
        public string QueryHash { get; set; } = String.Empty;

        /// <summary>
        /// Provider name.
        /// </summary>
        public string Provider { get; set; } = String.Empty;

        /// <summary>
        /// Latitude, or null when nothing matched.
        /// </summary>
        public double? Lat { get; set; } = null;

        /// <summary>
        /// Longitude, or null when nothing matched.
        /// </summary>
        public double? Lng { get; set; } = null;

        /// <summary>
        /// IANA time zone, or null.
        /// </summary>
        public string? TimeZone { get; set; } = null;

        /// <summary>
        /// Formatted label, or null.
        /// </summary>
        public string? Formatted { get; set; } = null;

        /// <summary>
        /// Whether the provider matched anything.
        /// </summary>
        public bool Found { get; set; } = true;

        /// <summary>
        /// Expiry.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
