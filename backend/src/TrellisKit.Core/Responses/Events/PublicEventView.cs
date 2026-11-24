namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An event for the anonymous public routes (docs/PHASE4_CONTRACT.md section 4.9): EventSummary plus description, without RSVP, viewer, attendee data, or join URLs.
    /// </summary>
    public class PublicEventView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (evt_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Slug.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Absolute URL of the event's public landing page (published or canceled Public events only), or null
        /// (docs/PHASE4_CONTRACT.md section 18).
        /// </summary>
        public string? PublicUrl { get; set; } = null;

        /// <summary>
        /// Summary.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Category, or null.
        /// </summary>
        public PublicOption? Category { get; set; } = null;

        /// <summary>
        /// Format.
        /// </summary>
        public EventFormatEnum Format { get; set; } = EventFormatEnum.InPerson;

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
        /// All-day event.
        /// </summary>
        public bool IsAllDay { get; set; } = false;

        /// <summary>
        /// Location, or null.
        /// </summary>
        public EventLocationView? Location { get; set; } = null;

        /// <summary>
        /// Online platform label, or null.
        /// </summary>
        public string? OnlinePlatformLabel { get; set; } = null;

        /// <summary>
        /// Capacity, or null.
        /// </summary>
        public int? Capacity { get; set; } = null;

        /// <summary>
        /// Going RSVPs.
        /// </summary>
        public int GoingCount { get; set; } = 0;

        /// <summary>
        /// Spots left, or null.
        /// </summary>
        public int? SpotsLeft { get; set; } = null;

        /// <summary>
        /// Cover image, or null.
        /// </summary>
        public MediaView? Cover { get; set; } = null;

        /// <summary>
        /// Audience (always Public).
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Public;

        /// <summary>
        /// Recurrence, or null.
        /// </summary>
        public EventRecurrenceView? Recurrence { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// When published.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        #endregion
    }
}
