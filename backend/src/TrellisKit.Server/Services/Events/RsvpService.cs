namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Settings;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// RSVPs (docs/PHASE4_CONTRACT.md section 4.3): the member PUT and DELETE, staff actions (promote, move to the
    /// waitlist, remove, check in, undo), and waitlist promotion notices (EventWaitlistPromoted, in-app, push, and an
    /// Essential email). The decision runs inside one transaction in the data layer. Thread safety: stateless.
    /// </summary>
    public class RsvpService
    {
        #region Public-Members

        /// <summary>
        /// Clock (tests replace it).
        /// </summary>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? throw new ArgumentNullException(nameof(Clock)); }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public RsvpService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// PUT /api/v1/events/{id}/rsvp.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="eventId">Event.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The event with myRsvp.</returns>
        /// <exception cref="ApiErrorException">400, 403 RsvpNotAllowed or Muted, 404, 409 EventFull, RsvpClosed, EventCanceled, or RsvpLimitReached, 429.</exception>
        public async Task<EventView> SetAsync(ContentActor actor, string eventId, EventRsvpRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            string userId = actor.RequireUserId();
            EventRecord e = await RequireRsvpableAsync(actor, eventId, token).ConfigureAwait(false);
            if (request.Status == null || request.Status == EventRsvpStatusEnum.Waitlisted) throw ApiErrorException.Field("status", "Use Going or Interested.");
            int guests = request.GuestCount ?? 0;
            if (guests < 0 || guests > e.MaxGuestsPerRsvp) throw ApiErrorException.Field("guestCount", "Use 0 to " + e.MaxGuestsPerRsvp + " guests.");
            if (request.Status == EventRsvpStatusEnum.Interested) guests = 0;
            EventSettings s = _Services.Settings.Events;
            _Services.Limits.CheckMember(actor, "event-rsvps-day", s.RsvpsPerDay, RateLimitService.Day);
            _Services.Limits.EnforceMember(actor, "event-rsvps-min", s.RsvpsPerMinute, RateLimitService.Minute);
            _Services.Limits.EnforceMember(actor, "event-rsvps-day", s.RsvpsPerDay, RateLimitService.Day);
            DateTime now = _Clock();
            EventRsvp? existing = await _Services.Database.Events.Rsvps.ReadAsync(e.Id, userId, token).ConfigureAwait(false);
            if (existing == null && !actor.IsSystemAdmin)
            {
                long active = await _Services.Database.Events.Rsvps.CountActiveForUserAsync(userId, now, token).ConfigureAwait(false);
                if (active >= s.MaxActiveRsvpsPerMember) throw new ApiErrorException(409, "RsvpLimitReached", "You already have " + active + " RSVPs on upcoming events.");
            }
            bool hide = request.HideFromAttendeeLists ?? existing?.HideFromAttendeeLists ?? false;
            EventRsvpOutcome outcome = await _Services.Database.Events.Rsvps.SetAsync(e.Id, userId, request.Status.Value, guests, hide, now, PromoteBefore(e), token).ConfigureAwait(false);
            switch (outcome.Decision)
            {
                case EventRsvpDecisionEnum.Full:
                    EventTelemetry.Count(EventTelemetry.Rsvps, 1, "action", "going", "outcome", "full");
                    throw new ApiErrorException(409, "EventFull", existing != null && existing.Status == EventRsvpStatusEnum.Going ? "There are not enough spots for your guests." : "This event is full.");
                case EventRsvpDecisionEnum.Closed:
                    throw new ApiErrorException(409, "RsvpClosed", "RSVPs are closed for this event.");
            }
            EventTelemetry.Count(EventTelemetry.Rsvps, 1, "action", outcome.Decision.ToString().ToLowerInvariant(), "outcome", "success");
            await AfterPromotionAsync(e, outcome.PromotedUserIds, token).ConfigureAwait(false);
            return await ViewAsync(actor, e.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/events/{id}/rsvp ("Not going"); allowed until the start.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="eventId">Event.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The event without myRsvp.</returns>
        /// <exception cref="ApiErrorException">404, 409 RsvpClosed after the start, 429.</exception>
        public async Task<EventView> DeleteAsync(ContentActor actor, string eventId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            EventRecord e = await RequireReadableAsync(actor, eventId, token).ConfigureAwait(false);
            DateTime now = _Clock();
            if (now >= e.StartUtc) throw new ApiErrorException(409, "RsvpClosed", "The event already started.");
            _Services.Limits.EnforceMember(actor, "event-rsvps-min", _Services.Settings.Events.RsvpsPerMinute, RateLimitService.Minute);
            EventRsvpOutcome outcome = await _Services.Database.Events.Rsvps.DeleteAsync(e.Id, userId, now, PromoteBefore(e), token).ConfigureAwait(false);
            if (outcome.Decision == EventRsvpDecisionEnum.Removed) EventTelemetry.Count(EventTelemetry.Rsvps, 1, "action", "remove", "outcome", "success");
            await AfterPromotionAsync(e, outcome.PromotedUserIds, token).ConfigureAwait(false);
            return await ViewAsync(actor, e.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// A staff or host action on an RSVP (promote, move-to-waitlist, remove, check-in, undo-check-in).
        /// </summary>
        /// <param name="actor">Event staff (any action) or a host (check-in and undo).</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="eventId">Event.</param>
        /// <param name="rsvpId">RSVP.</param>
        /// <param name="action">Action.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The RSVP afterwards (a removed RSVP is returned as it was).</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<AdminRsvpView> StaffActionAsync(ContentActor actor, RequestInfo info, string eventId, string rsvpId, string action, EventRsvpActionRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            EventRecord e = await _Services.Events.Catalog.RequireManagedAsync(actor, eventId, true, token).ConfigureAwait(false);
            bool checkIn = action == "check-in" || action == "undo-check-in";
            if (!checkIn && !actor.IsEventStaff) throw ApiErrorException.Forbidden("Only event staff manage RSVPs; hosts may check people in.");
            EventRsvp before = await _Services.Database.Events.Rsvps.ReadByIdAsync(e.Id, rsvpId, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("RSVP");
            DateTime now = _Clock();
            EventRsvp? after;
            bool notify = request.NotifyMember ?? true;
            if (checkIn)
            {
                if (action == "check-in" && before.Status != EventRsvpStatusEnum.Going) throw new ApiErrorException(409, "NotGoing", "Only Going RSVPs can be checked in.");
                after = await _Services.Database.Events.Rsvps.CheckInAsync(e.Id, rsvpId, action == "check-in", actor.UserId, now, token).ConfigureAwait(false);
                if (action == "check-in") EventTelemetry.Count(EventTelemetry.Rsvps, 1, "action", "check_in", "outcome", "success");
                await _Services.Audit.WriteAsync(info, action == "check-in" ? "event.checkIn" : "event.undoCheckIn", "EventRsvp", rsvpId, "event " + e.Id, token).ConfigureAwait(false);
            }
            else
            {
                EventRsvpOutcome outcome = await _Services.Database.Events.Rsvps.StaffActionAsync(e.Id, rsvpId, action, now, PromoteBefore(e), token).ConfigureAwait(false);
                if (outcome.Decision == EventRsvpDecisionEnum.Missing) throw ApiErrorException.NotFound("RSVP");
                after = outcome.Rsvp;
                string audit = action == "promote" ? "event.rsvp.promote" : action == "remove" ? "event.rsvp.remove" : "event.rsvp.moveToWaitlist";
                await _Services.Audit.WriteAsync(info, audit, "EventRsvp", rsvpId, "event " + e.Id + (String.IsNullOrWhiteSpace(request.Reason) ? String.Empty : "; " + request.Reason.Trim()), token).ConfigureAwait(false);
                EventTelemetry.Count(EventTelemetry.Rsvps, 1, "action", action == "promote" ? "promote" : action == "remove" ? "remove" : "waitlisted", "outcome", "staff");
                if (action == "promote" && notify && before.Status != EventRsvpStatusEnum.Going) await _Services.Events.Notifier.WaitlistPromotedAsync(e, before.UserId, token).ConfigureAwait(false);
                await AfterPromotionAsync(e, outcome.PromotedUserIds, token).ConfigureAwait(false);
            }
            return await _Services.Events.Catalog.AdminRsvpViewAsync(after ?? before, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Promote from the waitlist after an edit made room (capacity raised or removed, waitlist turned on).
        /// </summary>
        /// <param name="e">Event (as stored after the edit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Members promoted.</returns>
        public async Task<int> PromoteAfterChangeAsync(EventRecord e, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(e);
            EventRsvpOutcome outcome = await _Services.Database.Events.Rsvps.PromoteAsync(e.Id, _Clock(), PromoteBefore(e), token).ConfigureAwait(false);
            await AfterPromotionAsync(e, outcome.PromotedUserIds, token).ConfigureAwait(false);
            return outcome.PromotedUserIds.Count;
        }

        /// <summary>
        /// Delete a member's RSVP on one event with promotion (account lifecycle).
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task RemoveForLifecycleAsync(string eventId, string userId, CancellationToken token = default)
        {
            EventRecord? e = await _Services.Database.Events.Records.ReadAsync(eventId, token).ConfigureAwait(false);
            DateTime now = _Clock();
            DateTime cutoff = e == null ? now : PromoteBefore(e);
            EventRsvpOutcome outcome = await _Services.Database.Events.Rsvps.DeleteAsync(eventId, userId, now, cutoff, token).ConfigureAwait(false);
            if (e != null) await AfterPromotionAsync(e, outcome.PromotedUserIds, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private DateTime PromoteBefore(EventRecord e)
        {
            return DateTime.SpecifyKind(e.StartUtc.AddMinutes(-_Services.Settings.Events.WaitlistCutoffMinutes), DateTimeKind.Utc);
        }

        private async Task AfterPromotionAsync(EventRecord e, List<string> promoted, CancellationToken token)
        {
            foreach (string userId in promoted)
            {
                EventTelemetry.Count(EventTelemetry.Rsvps, 1, "action", "promote", "outcome", "waitlist");
                await _Services.Events.Notifier.WaitlistPromotedAsync(e, userId, token).ConfigureAwait(false);
            }
        }

        private async Task<EventRecord> RequireReadableAsync(ContentActor actor, string eventId, CancellationToken token)
        {
            EventRecord? e = await _Services.Database.Events.Records.ReadAsync(eventId, token).ConfigureAwait(false);
            if (e == null) throw ApiErrorException.NotFound("Event");
            EventReadEnum read = EventAccess.Read(e, actor);
            if (read == EventReadEnum.Hidden) throw ApiErrorException.NotFound("Event");
            if (read == EventReadEnum.Locked) throw new ApiErrorException(403, "RsvpNotAllowed", "A membership is required to RSVP to this event.");
            return e;
        }

        private async Task<EventRecord> RequireRsvpableAsync(ContentActor actor, string eventId, CancellationToken token)
        {
            EventRecord e = await RequireReadableAsync(actor, eventId, token).ConfigureAwait(false);
            if (e.Status == EventStatusEnum.Canceled) throw new ApiErrorException(409, "EventCanceled", "This event was canceled.");
            if (e.Status != EventStatusEnum.Published) throw ApiErrorException.NotFound("Event");
            if (!EventAccess.MayRsvp(e, actor)) throw new ApiErrorException(403, "RsvpNotAllowed", e.RsvpAudience == ContentAudienceEnum.Members ? "Only members can RSVP to this event." : "You cannot RSVP to this event.");
            actor.RequireNotMuted();
            if (!EventAccess.RsvpOpen(e, _Clock())) throw new ApiErrorException(409, "RsvpClosed", "RSVPs are closed for this event.");
            return e;
        }

        private async Task<EventView> ViewAsync(ContentActor actor, string eventId, CancellationToken token)
        {
            EventRecord e = await _Services.Database.Events.Records.ReadAsync(eventId, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Event");
            return await _Services.Events.Views.DetailAsync(actor, e, token).ConfigureAwait(false);
        }

        #endregion
    }
}
