namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// RSVPs (docs/PHASE4_CONTRACT.md section 4.3). Every write is one transaction that locks the event row
    /// (SELECT ... FOR UPDATE), recounts Going plus guests from the RSVP rows, decides the status, writes the RSVP and
    /// the counters, and promotes Waitlisted RSVPs whose party fits (oldest first) while the waitlist is open.
    /// Every statement is property scoped. Thread safety: concurrent writes on one event serialize on its row lock.
    /// </summary>
    public interface IEventRsvpMethods
    {
        /// <summary>
        /// Set a member's RSVP (Going or Interested). Going on a full event waitlists when waitlistEnabled, else
        /// decides Full and writes nothing. Events that are not Published, or have started, decide Closed.
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <param name="userId">Member.</param>
        /// <param name="status">Going or Interested.</param>
        /// <param name="guestCount">Guests.</param>
        /// <param name="hideFromAttendeeLists">Hide from attendee lists.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="promoteBeforeUtc">Waitlist promotion runs only while now is before this (start minus the cutoff).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome.</returns>
        Task<EventRsvpOutcome> SetAsync(string eventId, string userId, EventRsvpStatusEnum status, int guestCount, bool hideFromAttendeeLists, DateTime nowUtc, DateTime promoteBeforeUtc, CancellationToken token = default);

        /// <summary>
        /// Delete a member's RSVP and promote from the waitlist.
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="promoteBeforeUtc">Promotion cutoff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome (Removed or Missing).</returns>
        Task<EventRsvpOutcome> DeleteAsync(string eventId, string userId, DateTime nowUtc, DateTime promoteBeforeUtc, CancellationToken token = default);

        /// <summary>
        /// A staff action on one RSVP: promote to Going (even beyond capacity), move to the waitlist (end of the
        /// queue; excluded from this transaction's promotion), or remove; then promote from the waitlist.
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <param name="rsvpId">RSVP.</param>
        /// <param name="action">promote, move-to-waitlist, or remove.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="promoteBeforeUtc">Promotion cutoff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome (Missing when the RSVP is not on the event).</returns>
        /// <exception cref="ArgumentException">Unknown action.</exception>
        Task<EventRsvpOutcome> StaffActionAsync(string eventId, string rsvpId, string action, DateTime nowUtc, DateTime promoteBeforeUtc, CancellationToken token = default);

        /// <summary>
        /// Promote from the waitlist (after capacity was raised or the waitlist turned on).
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="promoteBeforeUtc">Promotion cutoff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome with the promoted members.</returns>
        Task<EventRsvpOutcome> PromoteAsync(string eventId, DateTime nowUtc, DateTime promoteBeforeUtc, CancellationToken token = default);

        /// <summary>
        /// Check a Going RSVP in or undo the check-in (counters recomputed).
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <param name="rsvpId">RSVP.</param>
        /// <param name="checkIn">True to check in, false to undo.</param>
        /// <param name="byUserId">Staff member or host, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The RSVP afterwards, or null when not on the event.</returns>
        Task<EventRsvp?> CheckInAsync(string eventId, string rsvpId, bool checkIn, string? byUserId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// A member's RSVP on an event (with waitlist position), or null.
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>RSVP or null.</returns>
        Task<EventRsvp?> ReadAsync(string eventId, string userId, CancellationToken token = default);

        /// <summary>
        /// An RSVP by id on an event (with waitlist position), or null.
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <param name="rsvpId">RSVP.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>RSVP or null.</returns>
        Task<EventRsvp?> ReadByIdAsync(string eventId, string rsvpId, CancellationToken token = default);

        /// <summary>
        /// A member's RSVPs on many events (with waitlist positions), by event id.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="eventIds">Events.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>RSVPs by event id.</returns>
        Task<Dictionary<string, EventRsvp>> ReadForUserAsync(string userId, IEnumerable<string> eventIds, CancellationToken token = default);

        /// <summary>
        /// One page of an event's RSVPs (waitlist positions filled).
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <param name="statuses">Statuses (any).</param>
        /// <param name="checkedIn">Only checked in (true), not checked in (false), or either (null).</param>
        /// <param name="includeHidden">Include members who hide from attendee lists.</param>
        /// <param name="request">Normalized enumeration request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<EventRsvp>> EnumerateAsync(string eventId, List<EventRsvpStatusEnum> statuses, bool? checkedIn, bool includeHidden, EnumerationRequest request, CancellationToken token = default);

        /// <summary>
        /// The first Going members of an event (oldest RSVP first) and the total for the attendee preview.
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <param name="includeHidden">Include members who hide from attendee lists.</param>
        /// <param name="limit">Most members.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member ids.</returns>
        Task<List<string>> PreviewAsync(string eventId, bool includeHidden, int limit, CancellationToken token = default);

        /// <summary>
        /// Going members of an event visible in attendee lists.
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <param name="includeHidden">Include members who hide from attendee lists.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountGoingAsync(string eventId, bool includeHidden, CancellationToken token = default);

        /// <summary>
        /// A member's RSVPs on events that have not started.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountActiveForUserAsync(string userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Members with an RSVP of the given statuses on an event, by user id after a cursor (fan-out pages).
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <param name="statuses">Statuses.</param>
        /// <param name="afterUserId">Cursor, or null.</param>
        /// <param name="limit">Most members.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>RSVPs ordered by user id.</returns>
        Task<List<EventRsvp>> ListRecipientsAsync(string eventId, List<EventRsvpStatusEnum> statuses, string? afterUserId, int limit, CancellationToken token = default);

        /// <summary>
        /// A member's RSVPs with their events, by RSVP id after a cursor (exports and account lifecycle).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="afterId">Cursor, or null.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        Task<List<EventRsvpRecord>> ListForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Delete every RSVP of a member on events that already started and recompute those events' counters
        /// (account deletion; upcoming ones are deleted one by one with promotion).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteStartedForUserAsync(string userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Every RSVP of an event (any status).
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count of RSVPs on the event.</returns>
        Task<long> CountForEventAsync(string eventId, CancellationToken token = default);
    }
}
