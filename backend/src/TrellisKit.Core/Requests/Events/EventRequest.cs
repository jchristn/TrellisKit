namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Create or edit an event (docs/PHASE4_CONTRACT.md section 4.7; recurrence and scope from resolved decision 11; proposals from resolved decision 10). On PUT omitted fields stay.
    /// </summary>
    public class EventRequest
    {
        #region Public-Members

        /// <summary>
        /// Title (3 to 140).
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Slug (generated from the title when omitted).
        /// </summary>
        public string? Slug { get; set; } = null;

        /// <summary>
        /// Summary (0 to 300).
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Description (Markdown subset, 0 to events.maxDescriptionLength).
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Category key, or null.
        /// </summary>
        public string? Category { get; set; } = null;

        /// <summary>
        /// InPerson, Online, or Hybrid.
        /// </summary>
        public EventFormatEnum? Format { get; set; } = null;

        /// <summary>
        /// Local start in the event zone (yyyy-MM-ddTHH:mm, or yyyy-MM-dd for all-day events).
        /// </summary>
        public string? StartLocal { get; set; } = null;

        /// <summary>
        /// Local end in the event zone.
        /// </summary>
        public string? EndLocal { get; set; } = null;

        /// <summary>
        /// IANA time zone (default the venue's zone).
        /// </summary>
        public string? TimeZone { get; set; } = null;

        /// <summary>
        /// All-day event (start and end are local dates; the end date is inclusive).
        /// </summary>
        public bool? IsAllDay { get; set; } = null;

        /// <summary>
        /// Location (InPerson and Hybrid).
        /// </summary>
        public EventLocationRequest? Location { get; set; } = null;

        /// <summary>
        /// Online details (Online and Hybrid).
        /// </summary>
        public EventOnlineRequest? Online { get; set; } = null;

        /// <summary>
        /// Capacity 1 to 100000, or null for unlimited.
        /// </summary>
        public int? Capacity { get; set; } = null;

        /// <summary>
        /// Guests per Going RSVP (0 to 10).
        /// </summary>
        public int? MaxGuestsPerRsvp { get; set; } = null;

        /// <summary>
        /// Waitlist when full (default true).
        /// </summary>
        public bool? WaitlistEnabled { get; set; } = null;

        /// <summary>
        /// SignedIn or Members.
        /// </summary>
        public ContentAudienceEnum? RsvpAudience { get; set; } = null;

        /// <summary>
        /// RSVP window start, or null.
        /// </summary>
        public DateTime? RsvpOpensUtc { get; set; } = null;

        /// <summary>
        /// RSVP window end, or null.
        /// </summary>
        public DateTime? RsvpClosesUtc { get; set; } = null;

        /// <summary>
        /// Everyone, Attendees (default), or Staff.
        /// </summary>
        public EventAttendeeVisibilityEnum? AttendeeVisibility { get; set; } = null;

        /// <summary>
        /// External https link, or null.
        /// </summary>
        public string? ExternalUrl { get; set; } = null;

        /// <summary>
        /// Cover image, or null.
        /// </summary>
        public string? CoverMediaId { get; set; } = null;

        /// <summary>
        /// Host members (at most events.maxHosts).
        /// </summary>
        public List<string>? HostUserIds { get; set; } = null;

        /// <summary>
        /// Directory instructors (at most 10).
        /// </summary>
        public List<string>? InstructorIds { get; set; } = null;

        /// <summary>
        /// 0 to 3 reminder offsets (5 to 20160 minutes).
        /// </summary>
        public List<int>? ReminderOffsetsMinutes { get; set; } = null;

        /// <summary>
        /// Comments on (default true).
        /// </summary>
        public bool? CommentsEnabled { get; set; } = null;

        /// <summary>
        /// Public (SystemAdmin only), SignedIn, or Members.
        /// </summary>
        public ContentAudienceEnum? Audience { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>
        /// PUT: send EventUpdated for time, location, or format changes (default true).
        /// </summary>
        public bool? NotifyAttendees { get; set; } = null;

        /// <summary>
        /// Recurrence rule (create, or PUT with scope Following or All), or null for a single event.
        /// </summary>
        public EventRecurrenceRequest? Recurrence { get; set; } = null;

        /// <summary>
        /// PUT on an occurrence: One (default), Following, or All.
        /// </summary>
        public EventEditScopeEnum? Scope { get; set; } = null;

        /// <summary>
        /// Proposals: a note to the reviewers (0 to 1000).
        /// </summary>
        public string? ProposalNote { get; set; } = null;

        #endregion
    }
}
