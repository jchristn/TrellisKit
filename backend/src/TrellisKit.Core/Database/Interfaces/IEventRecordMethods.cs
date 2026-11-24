namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Event rows (events, occurrences, proposals, series templates) with their hosts, instructors, and reminder
    /// offsets, plus the directory venue and instructor reads events need (docs/PHASE4_CONTRACT.md section 14).
    /// Every statement is property scoped.
    /// </summary>
    public interface IEventRecordMethods
    {
        /// <summary>
        /// Insert events in one transaction with their hosts, instructors, and reminders (due times from the start).
        /// </summary>
        /// <param name="events">Events.</param>
        /// <param name="nowUtc">Now (reminders already due are created skipped).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">A slug or occurrence key is taken.</exception>
        Task CreateManyAsync(List<EventRecord> events, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Read an undeleted event with its children, or null.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Event or null.</returns>
        Task<EventRecord?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read an undeleted event by slug (case-insensitive), or null.
        /// </summary>
        /// <param name="slug">Slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Event or null.</returns>
        Task<EventRecord?> ReadBySlugAsync(string slug, CancellationToken token = default);

        /// <summary>
        /// Read undeleted events with their children.
        /// </summary>
        /// <param name="ids">Identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Events (any order).</returns>
        Task<List<EventRecord>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Which slugs are taken by undeleted events (case-insensitive).
        /// </summary>
        /// <param name="slugs">Candidates.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Taken slugs (lower case).</returns>
        Task<HashSet<string>> TakenSlugsAsync(IEnumerable<string> slugs, CancellationToken token = default);

        /// <summary>
        /// Write an event's fields (not its RSVP, comment, reaction, or report counters), optionally replacing hosts,
        /// instructors, and reminder offsets. Reminders not yet due get due times from the new start with sentutc and
        /// skippedutc cleared.
        /// </summary>
        /// <param name="record">Event.</param>
        /// <param name="replaceChildren">Replace hosts, instructors, and reminders from the record.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the row exists.</returns>
        /// <exception cref="TrellisKit.Core.Exceptions.DuplicateRecordException">The slug or occurrence key is taken.</exception>
        Task<bool> UpdateAsync(EventRecord record, bool replaceChildren, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Soft delete events (and their reminders).
        /// </summary>
        /// <param name="ids">Identifiers.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteAsync(IEnumerable<string> ids, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// One page of events matching a filter.
        /// </summary>
        /// <param name="filter">Conditions.</param>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page (children loaded).</returns>
        Task<EnumerationResult<EventRecord>> EnumerateAsync(EventListFilter filter, EnumerationRequest request, CancellationToken token = default);

        /// <summary>
        /// How many events of a filter can be placed on the map (not Online, with a position, and not LocalityOnly for the caller).
        /// </summary>
        /// <param name="filter">Conditions (Box is the viewport).</param>
        /// <param name="request">Normalized enumeration request (labels, tags, search).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountMapAsync(EventListFilter filter, EnumerationRequest request, CancellationToken token = default);

        /// <summary>
        /// The map events of a filter with the address visibility the caller gets, by start.
        /// </summary>
        /// <param name="filter">Conditions (Box is the viewport).</param>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="ids">Only these events, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Points (children loaded).</returns>
        Task<List<EventMapPoint>> MapPointsAsync(EventListFilter filter, EnumerationRequest request, int limit, IEnumerable<string>? ids = null, CancellationToken token = default);

        /// <summary>
        /// Grid clusters of the map events of a filter (Approximate events at their rounded position).
        /// </summary>
        /// <param name="filter">Conditions (Box is the viewport).</param>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="cellDegrees">Grid cell size in degrees.</param>
        /// <param name="limit">Most cells.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Non-empty cells, largest first.</returns>
        Task<List<MapCluster>> MapClustersAsync(EventListFilter filter, EnumerationRequest request, double cellDegrees, int limit, CancellationToken token = default);

        /// <summary>
        /// Every undeleted occurrence of a recurrence (bounded by events.maxOccurrences), by start.
        /// </summary>
        /// <param name="recurrenceId">Recurrence.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Occurrences (children loaded), excluding the series template.</returns>
        Task<List<EventRecord>> ListOccurrencesAsync(string recurrenceId, int limit, CancellationToken token = default);

        /// <summary>
        /// Whether a member hosts an event.
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a host.</returns>
        Task<bool> IsHostAsync(string eventId, string userId, CancellationToken token = default);

        /// <summary>
        /// Remove a member as a host of every event (account deletion).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Host rows removed.</returns>
        Task<int> RemoveHostEverywhereAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Proposals of a member still waiting for review.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountPendingProposalsAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Undeleted events signed-out visitors may see (Public audience, not moderated, Published or Canceled) that use
        /// a cover image; templates of recurring series are not counted.
        /// </summary>
        /// <param name="mediaId">Cover media identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountPublicWithCoverAsync(string mediaId, CancellationToken token = default);

        /// <summary>
        /// Recompute the RSVP counters of events from the RSVP rows.
        /// </summary>
        /// <param name="ids">Events.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task RecomputeCountersAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Set the stored comment count, reaction total, or open report count of an event (from the shared services).
        /// </summary>
        /// <param name="id">Event.</param>
        /// <param name="column">commentcount, reactiontotal, or openreportcount.</param>
        /// <param name="value">Value.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentException">The column is not one of the three.</exception>
        Task SetCounterAsync(string id, string column, int value, CancellationToken token = default);

        /// <summary>
        /// Recount an event's visible comments, reactions, and open reports from their rows.
        /// </summary>
        /// <param name="id">Event.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task RecountEngagementAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Events a member is Going to or Waitlisted on starting in a window (calendar feed), by start.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="fromUtc">Window start.</param>
        /// <param name="toUtc">Window end.</param>
        /// <param name="limit">Most events.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Events.</returns>
        Task<List<EventRecord>> ListFeedEventsAsync(string userId, DateTime fromUtc, DateTime toUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Event ids changed at or after a time (search sweeps), at most limit.
        /// </summary>
        /// <param name="sinceUtc">Since, or null for all.</param>
        /// <param name="limit">Most ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers.</returns>
        Task<List<string>> ChangedIdsAsync(DateTime? sinceUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Events starting per UTC day in a window (published or canceled, templates excluded).
        /// </summary>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="untilUtc">Window end.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows by day.</returns>
        Task<List<EventDayCount>> EventsByDayAsync(DateTime sinceUtc, DateTime untilUtc, CancellationToken token = default);

        /// <summary>
        /// RSVPs created per UTC day in a window.
        /// </summary>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="untilUtc">Window end.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows by day.</returns>
        Task<List<EventDayCount>> RsvpsByDayAsync(DateTime sinceUtc, DateTime untilUtc, CancellationToken token = default);

        /// <summary>
        /// Going RSVPs and checked-in RSVPs of events that ended in a window and had at least one check-in.
        /// </summary>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="untilUtc">Window end.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Two numbers: going (index 0) and checked in (index 1).</returns>
        Task<long[]> CheckInTotalsAsync(DateTime sinceUtc, DateTime untilUtc, CancellationToken token = default);

        /// <summary>
        /// Events and Going RSVPs per category of events starting in a window, most events first.
        /// </summary>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="untilUtc">Window end.</param>
        /// <param name="limit">Most categories.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        Task<List<EventCategoryCount>> CategoryCountsAsync(DateTime sinceUtc, DateTime untilUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Published upcoming events with RSVP changes since a time (staff digest).
        /// </summary>
        /// <param name="sinceUtc">Changes since.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="limit">Most events.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        Task<List<EventRsvpDigestRow>> RsvpDigestAsync(DateTime sinceUtc, DateTime nowUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Refresh the unread notices of a type about an event for some recipients (title replaced, moved to the top,
        /// and the push queued again): the one notice per event while unread aggregation of EventUpdated.
        /// </summary>
        /// <param name="type">Notification type.</param>
        /// <param name="eventId">Event.</param>
        /// <param name="recipientUserIds">Recipients.</param>
        /// <param name="title">New title.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Recipients whose unread notice was refreshed.</returns>
        Task<HashSet<string>> RefreshUnreadNoticesAsync(TrellisKit.Core.Enums.NotificationTypeEnum type, string eventId, IEnumerable<string> recipientUserIds, string title, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// A directory venue as events copy and show it, or null.
        /// </summary>
        /// <param name="venueId">Venue.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Venue or null.</returns>
        Task<EventVenueSnapshot?> ReadVenueAsync(string venueId, CancellationToken token = default);

        /// <summary>
        /// Directory venues by id.
        /// </summary>
        /// <param name="venueIds">Venues.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Venues by id.</returns>
        Task<Dictionary<string, EventVenueSnapshot>> ReadVenuesAsync(IEnumerable<string> venueIds, CancellationToken token = default);

        /// <summary>
        /// Directory instructors by id.
        /// </summary>
        /// <param name="instructorIds">Instructors.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Instructors by id.</returns>
        Task<Dictionary<string, EventInstructorSnapshot>> ReadInstructorsAsync(IEnumerable<string> instructorIds, CancellationToken token = default);
    }
}
