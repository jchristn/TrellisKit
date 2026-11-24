namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The conditions of an event enumeration, built by the event service from the caller and the query filters (docs/PHASE4_CONTRACT.md section 4.2). Series templates are always excluded.
    /// </summary>
    public class EventListFilter
    {
        #region Public-Members

        /// <summary>Restrict an association enumeration to one event, or null for an unrestricted event set.</summary>
        public string? EventId { get; set; } = null;

        /// <summary>
        /// Now (for the when windows).
        /// </summary>
        public DateTime NowUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Statuses to include (empty: Published, plus Canceled when IncludeCanceled).
        /// </summary>
        public List<EventStatusEnum> Statuses { get; set; } = new List<EventStatusEnum>();

        /// <summary>
        /// Include Canceled events with the default statuses.
        /// </summary>
        public bool IncludeCanceled { get; set; } = false;

        /// <summary>
        /// Audiences the caller may list, or null for any.
        /// </summary>
        public List<ContentAudienceEnum>? Audiences { get; set; } = null;

        /// <summary>
        /// Upcoming, Past, or All.
        /// </summary>
        public EventWhenEnum When { get; set; } = EventWhenEnum.Upcoming;

        /// <summary>
        /// Start at or after, or null.
        /// </summary>
        public DateTime? StartsAfterUtc { get; set; } = null;

        /// <summary>
        /// Start before, or null.
        /// </summary>
        public DateTime? StartsBeforeUtc { get; set; } = null;

        /// <summary>
        /// Category keys (any).
        /// </summary>
        public List<string> Categories { get; set; } = new List<string>();

        /// <summary>
        /// Formats (any).
        /// </summary>
        public List<EventFormatEnum> Formats { get; set; } = new List<EventFormatEnum>();

        /// <summary>
        /// Venue, or null.
        /// </summary>
        public string? VenueId { get; set; } = null;

        /// <summary>
        /// Host, or null.
        /// </summary>
        public string? HostUserId { get; set; } = null;

        /// <summary>
        /// Instructor, or null.
        /// </summary>
        public string? InstructorId { get; set; } = null;

        /// <summary>
        /// Recurrence, or null.
        /// </summary>
        public string? RecurrenceId { get; set; } = null;

        /// <summary>
        /// Near latitude, or null.
        /// </summary>
        public double? NearLat { get; set; } = null;

        /// <summary>
        /// Near longitude, or null.
        /// </summary>
        public double? NearLng { get; set; } = null;

        /// <summary>
        /// Near radius in kilometers, or null.
        /// </summary>
        public double? NearRadiusKm { get; set; } = null;

        /// <summary>
        /// With near or Box: also include Online events.
        /// </summary>
        public bool IncludeOnline { get; set; } = false;

        /// <summary>
        /// Bounding box of event positions (not combined with near; Online events only with IncludeOnline), or null.
        /// </summary>
        public GeoBox? Box { get; set; } = null;

        /// <summary>
        /// Map queries only: the venue audiences the caller may read (events at other venues stay off the map), or null for event staff (exact positions).
        /// </summary>
        public List<ContentAudienceEnum>? MapVenueAudiences { get; set; } = null;

        /// <summary>
        /// Member whose RSVP status filters (rsvpStatus), or null.
        /// </summary>
        public string? RsvpUserId { get; set; } = null;

        /// <summary>
        /// RSVP statuses of RsvpUserId (any).
        /// </summary>
        public List<EventRsvpStatusEnum> RsvpStatuses { get; set; } = new List<EventRsvpStatusEnum>();

        /// <summary>
        /// Only events RsvpUserId has no RSVP on.
        /// </summary>
        public bool RsvpNone { get; set; } = false;

        /// <summary>
        /// Only events with unlimited capacity or spots left.
        /// </summary>
        public bool HasSpots { get; set; } = false;

        /// <summary>
        /// Only events this member RSVPed to or hosts, or null.
        /// </summary>
        public string? MineUserId { get; set; } = null;

        /// <summary>
        /// Also include Draft events this member hosts, or null.
        /// </summary>
        public string? DraftHostUserId { get; set; } = null;

        /// <summary>
        /// Omit events whose hosts are all blocked by or blocking this member, or null.
        /// </summary>
        public string? BlockerUserId { get; set; } = null;

        /// <summary>
        /// Only events this member proposed, or null.
        /// </summary>
        public string? ProposedByUserId { get; set; } = null;

        /// <summary>
        /// Only events that started as member proposals.
        /// </summary>
        public bool OnlyProposals { get; set; } = false;

        /// <summary>
        /// Only events whose moderation state is None.
        /// </summary>
        public bool OnlyUnmoderated { get; set; } = true;

        /// <summary>
        /// Order by distance from near (DistanceAscending).
        /// </summary>
        public bool ByDistance { get; set; } = false;

        #endregion
    }
}
