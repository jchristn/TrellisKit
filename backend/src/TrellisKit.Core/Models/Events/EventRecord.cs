namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One row of the events table: an event, an occurrence of a recurring event, a proposal, or a series template (docs/PHASE4_CONTRACT.md section 14).
    /// </summary>
    public class EventRecord
    {
        #region Public-Members

        /// <summary>
        /// Identifier (evt_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Slug (unique among undeleted events of the property).
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Summary, or null.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Description (Markdown subset), or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Category key, or null.
        /// </summary>
        public string? CategoryKey { get; set; } = null;

        /// <summary>
        /// Format.
        /// </summary>
        public EventFormatEnum Format { get; set; } = EventFormatEnum.InPerson;

        /// <summary>
        /// Status.
        /// </summary>
        public EventStatusEnum Status { get; set; } = EventStatusEnum.Draft;

        /// <summary>
        /// Start (UTC).
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// End (UTC).
        /// </summary>
        public DateTime EndUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// IANA time zone of the event.
        /// </summary>
        public string TimeZone { get; set; } = "UTC";

        /// <summary>
        /// Whether start and end are local dates.
        /// </summary>
        public bool IsAllDay { get; set; } = false;

        /// <summary>
        /// Recurrence (erc_) this occurrence belongs to, or null.
        /// </summary>
        public string? RecurrenceId { get; set; } = null;

        /// <summary>
        /// The start the recurrence rule produced for this occurrence (the exception key), or null.
        /// </summary>
        public DateTime? OriginalStartUtc { get; set; } = null;

        /// <summary>
        /// Whether the occurrence was edited or canceled on its own.
        /// </summary>
        public bool IsException { get; set; } = false;

        /// <summary>
        /// Position of the occurrence in its rule (0 based), or null.
        /// </summary>
        public int? OccurrenceIndex { get; set; } = null;

        /// <summary>
        /// Directory venue, or null.
        /// </summary>
        public string? VenueId { get; set; } = null;

        /// <summary>
        /// Place name, or null.
        /// </summary>
        public string? LocationName { get; set; } = null;

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
        /// ISO 3166-1 alpha-2 country code.
        /// </summary>
        public string? CountryCode { get; set; } = null;

        /// <summary>
        /// Exact, Approximate, or LocalityOnly.
        /// </summary>
        public string AddressVisibility { get; set; } = "Exact";

        /// <summary>
        /// Latitude, or null.
        /// </summary>
        public double? Lat { get; set; } = null;

        /// <summary>
        /// Longitude, or null.
        /// </summary>
        public double? Lng { get; set; } = null;

        /// <summary>
        /// Online platform label, or null.
        /// </summary>
        public string? OnlinePlatformLabel { get; set; } = null;

        /// <summary>
        /// Online join URL (https), or null.
        /// </summary>
        public string? OnlineJoinUrl { get; set; } = null;

        /// <summary>
        /// Capacity, or null for unlimited.
        /// </summary>
        public int? Capacity { get; set; } = null;

        /// <summary>
        /// Guests a Going RSVP may bring (0 to 10).
        /// </summary>
        public int MaxGuestsPerRsvp { get; set; } = 0;

        /// <summary>
        /// Whether a full event waitlists Going requests.
        /// </summary>
        public bool WaitlistEnabled { get; set; } = true;

        /// <summary>
        /// Who may RSVP (SignedIn or Members).
        /// </summary>
        public ContentAudienceEnum RsvpAudience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// RSVP window start, or null (publish).
        /// </summary>
        public DateTime? RsvpOpensUtc { get; set; } = null;

        /// <summary>
        /// RSVP window end, or null (start).
        /// </summary>
        public DateTime? RsvpClosesUtc { get; set; } = null;

        /// <summary>
        /// Who may see the attendee list.
        /// </summary>
        public EventAttendeeVisibilityEnum AttendeeVisibility { get; set; } = EventAttendeeVisibilityEnum.Attendees;

        /// <summary>
        /// External https link, or null.
        /// </summary>
        public string? ExternalUrl { get; set; } = null;

        /// <summary>
        /// Cover image media, or null.
        /// </summary>
        public string? CoverMediaId { get; set; } = null;

        /// <summary>
        /// Whether members may comment.
        /// </summary>
        public bool CommentsEnabled { get; set; } = true;

        /// <summary>
        /// Going RSVPs.
        /// </summary>
        public int GoingCount { get; set; } = 0;

        /// <summary>
        /// Guests of Going RSVPs.
        /// </summary>
        public int GuestCount { get; set; } = 0;

        /// <summary>
        /// Interested RSVPs.
        /// </summary>
        public int InterestedCount { get; set; } = 0;

        /// <summary>
        /// Waitlisted RSVPs.
        /// </summary>
        public int WaitlistCount { get; set; } = 0;

        /// <summary>
        /// Checked-in RSVPs.
        /// </summary>
        public int CheckedInCount { get; set; } = 0;

        /// <summary>
        /// Visible comments.
        /// </summary>
        public int CommentCount { get; set; } = 0;

        /// <summary>
        /// Reactions on the event.
        /// </summary>
        public int ReactionTotal { get; set; } = 0;

        /// <summary>
        /// Open reports.
        /// </summary>
        public int OpenReportCount { get; set; } = 0;

        /// <summary>
        /// ICS SEQUENCE (incremented on each time, location, format, or status change).
        /// </summary>
        public int IcsSequence { get; set; } = 0;

        /// <summary>
        /// Who may read the event.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// Moderation state.
        /// </summary>
        public ModerationStateEnum ModerationState { get; set; } = ModerationStateEnum.None;

        /// <summary>
        /// When moderation last changed, or null.
        /// </summary>
        public DateTime? ModeratedUtc { get; set; } = null;

        /// <summary>
        /// Moderator, or null.
        /// </summary>
        public string? ModeratedByUserId { get; set; } = null;

        /// <summary>
        /// Moderation reason, or null.
        /// </summary>
        public string? ModerationReason { get; set; } = null;

        /// <summary>
        /// When canceled, or null.
        /// </summary>
        public DateTime? CanceledUtc { get; set; } = null;

        /// <summary>
        /// Cancellation reason, or null.
        /// </summary>
        public string? CancelReason { get; set; } = null;

        /// <summary>
        /// Who canceled, or null.
        /// </summary>
        public string? CanceledByUserId { get; set; } = null;

        /// <summary>
        /// Member who proposed the event, or null.
        /// </summary>
        public string? ProposedByUserId { get; set; } = null;

        /// <summary>
        /// Proposer's note to reviewers, or null.
        /// </summary>
        public string? ProposalNote { get; set; } = null;

        /// <summary>
        /// Reviewer's note, or null.
        /// </summary>
        public string? ReviewNote { get; set; } = null;

        /// <summary>
        /// Reviewer, or null.
        /// </summary>
        public string? ReviewerUserId { get; set; } = null;

        /// <summary>
        /// When reviewed, or null.
        /// </summary>
        public DateTime? ReviewedUtc { get; set; } = null;

        /// <summary>
        /// Feed card of a promotion, or null.
        /// </summary>
        public string? PromotedFeedPostId { get; set; } = null;

        /// <summary>
        /// When published, or null.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <summary>
        /// Creator, or null.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// When deleted, or null.
        /// </summary>
        public DateTime? DeletedUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Host member ids in order (filled by reads that load hosts).
        /// </summary>
        public List<string> HostUserIds { get; set; } = new List<string>();

        /// <summary>
        /// Directory instructor ids in order (filled by reads that load them).
        /// </summary>
        public List<string> InstructorIds { get; set; } = new List<string>();

        /// <summary>
        /// Reminder offsets in minutes, largest first (filled by reads that load them).
        /// </summary>
        public List<int> ReminderOffsetsMinutes { get; set; } = new List<int>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// A field-by-field copy (child lists copied too).
        /// </summary>
        /// <returns>Copy.</returns>
        public EventRecord Clone()
        {
            EventRecord copy = (EventRecord)MemberwiseClone();
            copy.HostUserIds = new List<string>(HostUserIds);
            copy.InstructorIds = new List<string>(InstructorIds);
            copy.ReminderOffsetsMinutes = new List<int>(ReminderOffsetsMinutes);
            return copy;
        }

        #endregion
    }
}
