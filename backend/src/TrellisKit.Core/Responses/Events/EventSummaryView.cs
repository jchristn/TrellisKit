namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An event in a list (EventSummary, docs/PHASE4_CONTRACT.md section 2.5).
    /// </summary>
    public class EventSummaryView : IMetadataPayload
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
        /// Category { key, label, color }, or null.
        /// </summary>
        public PublicOption? Category { get; set; } = null;

        /// <summary>
        /// Format.
        /// </summary>
        public EventFormatEnum Format { get; set; } = EventFormatEnum.InPerson;

        /// <summary>
        /// Status.
        /// </summary>
        public EventStatusEnum Status { get; set; } = EventStatusEnum.Published;

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
        /// Local start in the event zone (yyyy-MM-ddTHH:mm).
        /// </summary>
        public string StartLocal { get; set; } = String.Empty;

        /// <summary>
        /// Local end in the event zone (yyyy-MM-ddTHH:mm).
        /// </summary>
        public string EndLocal { get; set; } = String.Empty;

        /// <summary>
        /// Whether the event has ended.
        /// </summary>
        public bool IsPast { get; set; } = false;

        /// <summary>
        /// Location (InPerson and Hybrid), or null.
        /// </summary>
        public EventLocationView? Location { get; set; } = null;

        /// <summary>
        /// Online details (Online and Hybrid), or null.
        /// </summary>
        public EventOnlineView? Online { get; set; } = null;

        /// <summary>
        /// Capacity, or null for unlimited.
        /// </summary>
        public int? Capacity { get; set; } = null;

        /// <summary>
        /// Going RSVPs.
        /// </summary>
        public int GoingCount { get; set; } = 0;

        /// <summary>
        /// Interested RSVPs.
        /// </summary>
        public int InterestedCount { get; set; } = 0;

        /// <summary>
        /// Waitlisted RSVPs.
        /// </summary>
        public int WaitlistCount { get; set; } = 0;

        /// <summary>
        /// Capacity minus Going and guests (never negative), or null.
        /// </summary>
        public int? SpotsLeft { get; set; } = null;

        /// <summary>
        /// Whether a full event waitlists.
        /// </summary>
        public bool WaitlistEnabled { get; set; } = false;

        /// <summary>
        /// Who may RSVP.
        /// </summary>
        public ContentAudienceEnum RsvpAudience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// RSVP window start, or null.
        /// </summary>
        public DateTime? RsvpOpensUtc { get; set; } = null;

        /// <summary>
        /// RSVP window end, or null.
        /// </summary>
        public DateTime? RsvpClosesUtc { get; set; } = null;

        /// <summary>
        /// Who may see attendees.
        /// </summary>
        public EventAttendeeVisibilityEnum AttendeeVisibility { get; set; } = EventAttendeeVisibilityEnum.Attendees;

        /// <summary>
        /// External link, or null.
        /// </summary>
        public string? ExternalUrl { get; set; } = null;

        /// <summary>
        /// Cover image, or null.
        /// </summary>
        public MediaView? Cover { get; set; } = null;

        /// <summary>
        /// Hosts.
        /// </summary>
        public int HostCount { get; set; } = 0;

        /// <summary>
        /// The caller's RSVP, or null.
        /// </summary>
        public EventMyRsvpView? MyRsvp { get; set; } = null;

        /// <summary>
        /// Reminder offsets.
        /// </summary>
        public List<int> ReminderOffsetsMinutes { get; set; } = new List<int>();

        /// <summary>
        /// Comments on.
        /// </summary>
        public bool CommentsEnabled { get; set; } = false;

        /// <summary>
        /// Conversation target { type: Event, id }.
        /// </summary>
        public ContentTarget CommentTarget { get; set; } = new ContentTarget();

        /// <summary>
        /// Visible comments.
        /// </summary>
        public int CommentCount { get; set; } = 0;

        /// <summary>
        /// Reaction counts by key.
        /// </summary>
        public Dictionary<string, int> ReactionCounts { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// Reactions.
        /// </summary>
        public int ReactionTotal { get; set; } = 0;

        /// <summary>
        /// The caller's reaction keys.
        /// </summary>
        public List<string> MyReactions { get; set; } = new List<string>();

        /// <summary>
        /// Feed promotion, or null.
        /// </summary>
        public object? Promotion { get; set; } = null;

        /// <summary>
        /// Who may read the event.
        /// </summary>
        public ContentAudienceEnum Audience { get; set; } = ContentAudienceEnum.Members;

        /// <summary>
        /// When canceled, or null.
        /// </summary>
        public DateTime? CanceledUtc { get; set; } = null;

        /// <summary>
        /// Cancellation reason, or null.
        /// </summary>
        public string? CancelReason { get; set; } = null;

        /// <summary>
        /// Locked stub (description, online, attendees, hosts, and myRsvp empty).
        /// </summary>
        public bool Locked { get; set; } = false;

        /// <summary>
        /// Recurrence of an occurrence, or null for a single event.
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
        /// What the caller may do.
        /// </summary>
        public EventViewerView Viewer { get; set; } = new EventViewerView();

        /// <summary>
        /// When published, or null.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
