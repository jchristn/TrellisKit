namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An event on the events map (docs/PHASE4_CONTRACT.md section 4.2).
    /// </summary>
    public class MapEventView
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Slug.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Start (UTC).
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// End (UTC).
        /// </summary>
        public DateTime EndUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// IANA time zone.
        /// </summary>
        public string TimeZone { get; set; } = "UTC";

        /// <summary>
        /// Start in the event's time zone (yyyy-MM-ddTHH:mm).
        /// </summary>
        public string StartLocal { get; set; } = String.Empty;

        /// <summary>
        /// All-day event.
        /// </summary>
        public bool IsAllDay { get; set; } = false;

        /// <summary>
        /// InPerson or Hybrid (Online events are never on the map).
        /// </summary>
        public EventFormatEnum Format { get; set; } = EventFormatEnum.InPerson;

        /// <summary>
        /// Published or Canceled.
        /// </summary>
        public EventStatusEnum Status { get; set; } = EventStatusEnum.Published;

        /// <summary>
        /// Category, or null.
        /// </summary>
        public PublicOption? Category { get; set; } = null;

        /// <summary>
        /// Place name as the caller may see it, or null.
        /// </summary>
        public string? LocationName { get; set; } = null;

        /// <summary>
        /// Locality, or null.
        /// </summary>
        public string? Locality { get; set; } = null;

        /// <summary>
        /// Region, or null.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// ISO 3166-1 alpha-2 country, or null.
        /// </summary>
        public string? CountryCode { get; set; } = null;

        /// <summary>
        /// The venue when the caller may read it, or null.
        /// </summary>
        public EventVenueView? Venue { get; set; } = null;

        /// <summary>
        /// Position (Exact: 5 decimals; Approximate: 2 decimals).
        /// </summary>
        public EventGeoView Geo { get; set; } = new EventGeoView();

        /// <summary>
        /// Spots left, or null for unlimited capacity.
        /// </summary>
        public int? SpotsLeft { get; set; } = null;

        /// <summary>
        /// A locked stub (PartiallyGated): the caller may not open it.
        /// </summary>
        public bool Locked { get; set; } = false;

        /// <summary>
        /// Cover thumbnail URL, or null.
        /// </summary>
        public string? CoverThumbUrl { get; set; } = null;

        #endregion
    }
}
