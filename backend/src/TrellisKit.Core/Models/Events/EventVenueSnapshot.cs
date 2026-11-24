namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The fields of a directory venue an event copies and shows (read with the events data access).
    /// </summary>
    public class EventVenueSnapshot
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ven_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Slug.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Address line 1.
        /// </summary>
        public string? Line1 { get; set; } = null;

        /// <summary>
        /// Address line 2.
        /// </summary>
        public string? Line2 { get; set; } = null;

        /// <summary>
        /// Locality.
        /// </summary>
        public string? Locality { get; set; } = null;

        /// <summary>
        /// Region.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// Postal code.
        /// </summary>
        public string? PostalCode { get; set; } = null;

        /// <summary>
        /// Country code.
        /// </summary>
        public string? CountryCode { get; set; } = null;

        /// <summary>
        /// Address visibility.
        /// </summary>
        public string AddressVisibility { get; set; } = "Exact";

        /// <summary>
        /// Latitude.
        /// </summary>
        public double? Lat { get; set; } = null;

        /// <summary>
        /// Longitude.
        /// </summary>
        public double? Lng { get; set; } = null;

        /// <summary>
        /// IANA time zone, or null.
        /// </summary>
        public string? TimeZone { get; set; } = null;

        /// <summary>
        /// Audience.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Whether the venue is active, not deleted, and not moderated.
        /// </summary>
        public bool IsListed { get; set; } = false;

        #endregion
    }
}
