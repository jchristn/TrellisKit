namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The Phase 4 event enumeration collections (docs/PHASE4_CONTRACT.md sections 4.1, 4.2, and 4.7; occurrences from
    /// resolved decision 11; proposals from resolved decision 10). DistanceAscending is computed per request (the data
    /// methods replace its placeholder expression with the distance in meters from filters.near); Relevance orders like
    /// StartAscending among the search matches.
    /// </summary>
    public static class EventCollections
    {
        #region Public-Members

        /// <summary>
        /// Member event list filters.
        /// </summary>
        public const string EventFilters = "when, startsAfterUtc, startsBeforeUtc, category, format, venueId, hostUserId, instructorId, near, includeOnline, rsvpStatus, hasSpots, includeCanceled, audience, status, recurrenceId";

        /// <summary>
        /// Member event list.
        /// </summary>
        public static readonly EnumerationCollection Events = EventCollection("events", false)
            .Filters("when", "startsAfterUtc", "startsBeforeUtc", "category", "format", "venueId", "hostUserId", "instructorId", "near", "bbox", "includeOnline", "rsvpStatus", "hasSpots",
                "includeCanceled", "audience", "status", "recurrenceId")
            .DefaultTo("StartAscending");

        /// <summary>
        /// Events map features (bounded generated source, standard keyset pages).
        /// </summary>
        public static readonly EnumerationCollection Map = new EnumerationCollection("events-map", TargetTypeEnum.Event, "e.id", "e.createdutc", false)
            .Order("StartAscending", "e.startutc", false, EnumerationKeyKindEnum.Time)
            .Order("StartDescending", "e.startutc", true, EnumerationKeyKindEnum.Time)
            .Search("e.title", "e.summary", "e.description", "e.locationname", "e.locality")
            .Filters("when", "startsAfterUtc", "startsBeforeUtc", "category", "format", "venueId", "hostUserId", "instructorId", "rsvpStatus", "hasSpots", "includeCanceled", "audience")
            .DefaultTo("StartAscending");

        /// <summary>
        /// The occurrences of one recurrence the caller may see.
        /// </summary>
        public static readonly EnumerationCollection Occurrences = EventCollection("event-occurrences", false)
            .Filters("when", "startsAfterUtc", "startsBeforeUtc", "includeCanceled", "rsvpStatus")
            .DefaultTo("StartAscending");

        /// <summary>
        /// Events the caller RSVPed to or hosts.
        /// </summary>
        public static readonly EnumerationCollection MyEvents = EventCollection("me-events", false)
            .Filters("rsvpStatus", "upcoming", "past", "category", "format")
            .DefaultTo("StartAscending");

        /// <summary>
        /// Admin event list (every status the caller manages).
        /// </summary>
        public static readonly EnumerationCollection AdminEvents = EventCollection("admin-events", true)
            .Filters("when", "startsAfterUtc", "startsBeforeUtc", "category", "format", "venueId", "hostUserId", "instructorId", "near", "bbox", "includeOnline", "hasSpots",
                "includeCanceled", "audience", "status", "recurrenceId")
            .DefaultTo("StartAscending");

        /// <summary>
        /// Public events (anonymous).
        /// </summary>
        public static readonly EnumerationCollection PublicEvents = EventCollection("public-events", false)
            .Filters("startsAfterUtc", "startsBeforeUtc", "category", "format")
            .DefaultTo("StartAscending");

        /// <summary>
        /// Member proposals of the caller.
        /// </summary>
        public static readonly EnumerationCollection MyProposals = new EnumerationCollection("me-event-proposals", TargetTypeEnum.Event, "e.id", "e.createdutc", false)
            .Search("e.title", "e.summary")
            .Filters("status");

        /// <summary>
        /// Staff proposal queue (oldest first).
        /// </summary>
        public static readonly EnumerationCollection AdminProposals = new EnumerationCollection("admin-event-proposals", TargetTypeEnum.Event, "e.id", "e.createdutc", true)
            .Search("e.title", "e.summary", "e.description")
            .Filters("status", "proposedByUserId")
            .DefaultTo("CreatedAscending");

        /// <summary>
        /// Attendees of an event (member view).
        /// </summary>
        public static readonly EnumerationCollection Attendees = new EnumerationCollection("event-attendees", TargetTypeEnum.EventRsvp, "r.id", "r.createdutc", false)
            .Order("DisplayNameAscending", "lower(u.displayname)", false, EnumerationKeyKindEnum.Text)
            .Order("WaitlistAscending", "COALESCE(r.waitlistedutc, r.createdutc)", false, EnumerationKeyKindEnum.Time)
            .Search("u.displayname", "u.handle")
            .Filters("status")
            .DefaultTo("CreatedAscending");

        /// <summary>
        /// RSVPs of an event (event staff and hosts).
        /// </summary>
        public static readonly EnumerationCollection AdminRsvps = new EnumerationCollection("admin-event-rsvps", TargetTypeEnum.EventRsvp, "r.id", "r.createdutc", true)
            .Order("DisplayNameAscending", "lower(u.displayname)", false, EnumerationKeyKindEnum.Text)
            .Order("WaitlistAscending", "COALESCE(r.waitlistedutc, r.createdutc)", false, EnumerationKeyKindEnum.Time)
            .Order("UpdatedDescending", "r.updatedutc", true, EnumerationKeyKindEnum.Time)
            .Search("u.displayname", "u.handle")
            .Filters("status", "checkedIn")
            .DefaultTo("CreatedAscending");

        /// <summary>
        /// Event categories (member view: not retired).
        /// </summary>
        public static readonly EnumerationCollection Categories = new EnumerationCollection("event-categories", TargetTypeEnum.EventCategory, "c.id", "c.createdutc", false)
            .Order("SortOrderAscending", "c.sortorder", false, EnumerationKeyKindEnum.Number)
            .Order("LabelAscending", "lower(c.label)", false, EnumerationKeyKindEnum.Text)
            .Search("c.label", "c.key")
            .DefaultTo("SortOrderAscending");

        /// <summary>
        /// Event categories (admin view: retired included on request).
        /// </summary>
        public static readonly EnumerationCollection AdminCategories = new EnumerationCollection("admin-event-categories", TargetTypeEnum.EventCategory, "c.id", "c.createdutc", true)
            .Order("SortOrderAscending", "c.sortorder", false, EnumerationKeyKindEnum.Number)
            .Order("LabelAscending", "lower(c.label)", false, EnumerationKeyKindEnum.Text)
            .Search("c.label", "c.key")
            .Filters("includeRetired")
            .DefaultTo("SortOrderAscending");

        #endregion

        #region Private-Methods

        private static EnumerationCollection EventCollection(string name, bool isAdmin)
        {
            return new EnumerationCollection(name, TargetTypeEnum.Event, "e.id", "e.createdutc", isAdmin)
                .Order("StartAscending", "e.startutc", false, EnumerationKeyKindEnum.Time)
                .Order("StartDescending", "e.startutc", true, EnumerationKeyKindEnum.Time)
                .Order("DistanceAscending", "e.startutc", false, EnumerationKeyKindEnum.Number)
                .Order("Relevance", "e.startutc", false, EnumerationKeyKindEnum.Time)
                .Search("e.title", "e.summary", "e.description", "e.locationname", "e.locality");
        }

        #endregion
    }
}
