namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Server.Services.Email;
    using TrellisKit.Server.Services.Email.Templates;

    /// <summary>
    /// Event notifications and emails (docs/PHASE4_CONTRACT.md sections 4.4, 8, and 9; resolved decisions 10, 12, 14):
    /// NewEvent, EventUpdated (one per member per event while unread, also for a series edit), EventCanceled and
    /// EventWaitlistPromoted (Essential emails), EventReminder (push respects notification hours like every push; email
    /// for the largest offset only, governed by the Events email preference), EventProposalDecision, and the staff
    /// proposal and RSVP digest emails. Fan-outs run as idempotent notification jobs; sources are evt_x:... for one
    /// event and series:recurrence:ticks:target:flags for a series. Thread safety: stateless.
    /// </summary>
    public class EventNotifier
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
        public EventNotifier(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the fan-out processors.
        /// </summary>
        public void Register()
        {
            _Services.Notifications.FanoutHandlers[NotificationTypeEnum.NewEvent] = ProcessAsync;
            _Services.Notifications.FanoutHandlers[NotificationTypeEnum.EventUpdated] = ProcessAsync;
            _Services.Notifications.FanoutHandlers[NotificationTypeEnum.EventCanceled] = ProcessAsync;
            _Services.Notifications.FanoutHandlers[NotificationTypeEnum.EventReminder] = ProcessAsync;
        }

        /// <summary>
        /// The website link of an event.
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <returns>URL.</returns>
        public string EventLink(string eventId)
        {
            return _Services.Email.Link("/app/events/" + eventId);
        }

        /// <summary>
        /// The API origin (https://api domain, or the website origin when none is configured).
        /// </summary>
        /// <returns>Origin without a trailing slash.</returns>
        public string ApiOrigin()
        {
            return _Services.Property.ApiOrigin;
        }

        /// <summary>
        /// The ICS link of an event.
        /// </summary>
        /// <param name="eventId">Event.</param>
        /// <returns>URL.</returns>
        public string CalendarLink(string eventId)
        {
            return ApiOrigin() + Constants.ApiPrefix + "/events/" + eventId + "/ics";
        }

        /// <summary>
        /// Queue a fan-out for one event (NewEvent, EventUpdated with change flags, EventCanceled).
        /// </summary>
        /// <param name="type">Type.</param>
        /// <param name="e">Event.</param>
        /// <param name="flags">Change flags (EventUpdated), or empty.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when queued.</returns>
        public Task<bool> StartEventAsync(NotificationTypeEnum type, EventRecord e, string flags, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(e);
            string source = type == NotificationTypeEnum.NewEvent ? e.Id : e.Id + ":" + _Clock().Ticks.ToString(CultureInfo.InvariantCulture) + ":" + (flags ?? String.Empty);
            return _Services.Notifications.StartFanoutAsync(type, source, token);
        }

        /// <summary>
        /// Queue one aggregated fan-out for a series edit or cancellation: each member with an RSVP on an occurrence
        /// changed (or canceled) since the given time gets one notice that links to the target occurrence.
        /// </summary>
        /// <param name="type">EventUpdated or EventCanceled.</param>
        /// <param name="recurrenceId">Recurrence.</param>
        /// <param name="targetEventId">Occurrence the edit was made from.</param>
        /// <param name="sinceUtc">Changes at or after this time.</param>
        /// <param name="flags">Change flags.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when queued.</returns>
        public Task<bool> StartSeriesAsync(NotificationTypeEnum type, string recurrenceId, string targetEventId, DateTime sinceUtc, string flags, CancellationToken token = default)
        {
            string source = "series:" + recurrenceId + ":" + sinceUtc.Ticks.ToString(CultureInfo.InvariantCulture) + ":" + targetEventId + ":" + (flags ?? String.Empty);
            return _Services.Notifications.StartFanoutAsync(type, source, token);
        }

        /// <summary>
        /// A member was promoted from the waitlist: EventWaitlistPromoted in-app and push, and the Essential email.
        /// </summary>
        /// <param name="e">Event.</param>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task WaitlistPromotedAsync(EventRecord e, string userId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(e);
            if (String.IsNullOrEmpty(userId)) return;
            await _Services.Notifications.CreateAsync(Draft(NotificationTypeEnum.EventWaitlistPromoted, e, userId, "A spot opened: you are going to " + e.Title), token).ConfigureAwait(false);
            User? user = await _Services.Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) return;
            await QueueAsync(EventWaitlistPromotedEmail.TemplateKey, user, "event_waitlist_promoted:" + e.Id + ":" + userId + ":" + _Clock().Ticks.ToString(CultureInfo.InvariantCulture), false, token,
                "eventTitle", e.Title, "startLocal", EventTimeZones.Friendly(e.StartUtc, e.TimeZone, e.IsAllDay), "locationText", await LocationTextAsync(e, user, token).ConfigureAwait(false),
                "eventLink", EventLink(e.Id), "calendarLink", CalendarLink(e.Id)).ConfigureAwait(false);
        }

        /// <summary>
        /// Tell a proposer about the review decision (EventProposalDecision, category Account, and the Essential email).
        /// </summary>
        /// <param name="e">Event.</param>
        /// <param name="approved">Approved or rejected.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task ProposalDecisionAsync(EventRecord e, bool approved, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(e);
            if (String.IsNullOrEmpty(e.ProposedByUserId)) return;
            string title = approved ? "Your event proposal " + e.Title + " was approved" : "Your event proposal " + e.Title + " was not approved";
            NotificationDraft draft = Draft(NotificationTypeEnum.EventProposalDecision, e, e.ProposedByUserId, title);
            draft.Body = e.ReviewNote;
            if (!approved) draft.Path = "/me/events/proposals";
            await _Services.Notifications.CreateAsync(draft, token).ConfigureAwait(false);
            User? user = await _Services.Database.Users.ReadAsync(e.ProposedByUserId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) return;
            await QueueAsync(EventProposalDecisionEmail.TemplateKey, user, "event_proposal_decision:" + e.Id + ":" + (approved ? "approved" : "rejected"), false, token,
                "eventTitle", e.Title, "decision", approved ? "approved" : "not approved", "reviewNote", e.ReviewNote ?? String.Empty,
                "link", approved ? EventLink(e.Id) : _Services.Email.Link("/app/events/proposals")).ConfigureAwait(false);
        }

        /// <summary>
        /// Tell event staff about a new proposal (staff_event_proposal).
        /// </summary>
        /// <param name="e">Proposed event.</param>
        /// <param name="submitterName">Proposer's display name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task StaffProposalAsync(EventRecord e, string submitterName, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(e);
            string[] tokens = new[] { "eventTitle", e.Title, "submitterName", submitterName, "startLocal", EventTimeZones.Friendly(e.StartUtc, e.TimeZone, e.IsAllDay), "adminLink", _Services.Email.Link("/admin/events/proposals") };
            await _Services.Email.NotifyStaffAsync(StaffEventProposalEmail.TemplateKey, "staff_event_proposal:" + e.Id, token,
                (tokens[0], tokens[1]), (tokens[2], tokens[3]), (tokens[4], tokens[5]), (tokens[6], tokens[7])).ConfigureAwait(false);
            await EventManagersAsync(StaffEventProposalEmail.TemplateKey, "staff_event_proposal:" + e.Id, tokens, token).ConfigureAwait(false);
        }

        /// <summary>
        /// One pass of the staff RSVP digest: for each upcoming event with RSVP changes in the window, at most one email
        /// per event per events.staffRsvpDigestHours to the configured staff recipients.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Digests queued.</returns>
        public async Task<int> StaffDigestPassAsync(CancellationToken token = default)
        {
            DateTime now = _Clock();
            int hours = _Services.Settings.Events.StaffRsvpDigestHours;
            long bucket = now.Ticks / TimeSpan.FromHours(hours).Ticks;
            DateTime since = now.AddHours(-hours);
            int sent = 0;
            foreach (EventRsvpDigestRow row in await _Services.Database.Events.Records.RsvpDigestAsync(since, now, 200, token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                string key = "staff_event_rsvp_digest:" + row.EventId + ":" + bucket.ToString(CultureInfo.InvariantCulture);
                string[] tokens = new[] { "eventTitle", row.Title, "going", row.Going.ToString(CultureInfo.InvariantCulture), "waitlisted", row.Waitlisted.ToString(CultureInfo.InvariantCulture),
                    "changes", row.Changes.ToString(CultureInfo.InvariantCulture) + " RSVP changes in the last " + hours.ToString(CultureInfo.InvariantCulture) + " hours", "adminLink", _Services.Email.Link("/admin/events/" + row.EventId) };
                await _Services.Email.NotifyStaffAsync(StaffEventRsvpDigestEmail.TemplateKey, key, token, (tokens[0], tokens[1]), (tokens[2], tokens[3]), (tokens[4], tokens[5]), (tokens[6], tokens[7]), (tokens[8], tokens[9])).ConfigureAwait(false);
                await EventManagersAsync(StaffEventRsvpDigestEmail.TemplateKey, key, tokens, token).ConfigureAwait(false);
                sent++;
            }
            return sent;
        }

        /// <summary>
        /// Process one event fan-out job (registered for NewEvent, EventUpdated, EventCanceled, EventReminder).
        /// </summary>
        /// <param name="job">Job.</param>
        /// <param name="holder">Claim holder.</param>
        /// <param name="claimDuration">Claim extension.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Notifications created, or -1 when the claim was lost.</returns>
        public async Task<int> ProcessAsync(NotificationFanout job, string holder, TimeSpan claimDuration, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(job);
            string[] parts = job.SourceId.Split(':');
            bool series = parts.Length >= 5 && parts[0] == "series";
            string eventId = series ? parts[3] : parts[0];
            EventRecord? e = await _Services.Database.Events.Records.ReadAsync(eventId, token).ConfigureAwait(false);
            if (e == null) return 0;
            int batchSize = Math.Max(1, _Services.Settings.Notifications.FanoutBatchSize);
            switch (job.Type)
            {
                case NotificationTypeEnum.NewEvent:
                    return await NewEventAsync(job, e, holder, claimDuration, batchSize, token).ConfigureAwait(false);
                case NotificationTypeEnum.EventReminder:
                    return await ReminderAsync(job, e, parts, holder, claimDuration, batchSize, token).ConfigureAwait(false);
                default:
                    {
                        List<string> recipients;
                        string flags;
                        if (series)
                        {
                            DateTime since = new DateTime(Int64.Parse(parts[2], CultureInfo.InvariantCulture), DateTimeKind.Utc);
                            recipients = await SeriesRecipientsAsync(parts[1], since, job.Type == NotificationTypeEnum.EventCanceled, token).ConfigureAwait(false);
                            flags = parts[4];
                        }
                        else
                        {
                            recipients = await EventRecipientsAsync(e.Id, new List<EventRsvpStatusEnum> { EventRsvpStatusEnum.Going, EventRsvpStatusEnum.Waitlisted, EventRsvpStatusEnum.Interested }, token).ConfigureAwait(false);
                            flags = parts.Length > 2 ? parts[2] : String.Empty;
                        }
                        return await ChangeAsync(job, e, recipients, series, flags, holder, claimDuration, batchSize, token).ConfigureAwait(false);
                    }
            }
        }

        #endregion

        #region Private-Methods

        private async Task<int> NewEventAsync(NotificationFanout job, EventRecord e, string holder, TimeSpan claimDuration, int batchSize, CancellationToken token)
        {
            if (e.Status != EventStatusEnum.Published || e.ModerationState != ModerationStateEnum.None) return 0;
            AccessPolicy policy = _Services.Access.Current;
            bool membership = AccessDecider.RecipientsNeedMembership(policy, e.Audience);
            string author = e.CreatedByUserId ?? "usr_none";
            string? after = job.LastUserId;
            int created = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                List<FanoutRecipient> batch = await _Services.Database.Notifications.EnumerateFanoutRecipientsAsync(author, NotificationCategoryEnum.Events, true, after, batchSize, membership, _Clock(), token).ConfigureAwait(false);
                if (batch.Count == 0) break;
                List<NotificationDraft> drafts = batch.Where(r => r.InApp).Select(r => Draft(NotificationTypeEnum.NewEvent, e, r.UserId, "New event: " + e.Title)).ToList();
                int added = drafts.Count == 0 ? 0 : await _Services.Notifications.CreateManyAsync(drafts, true, token).ConfigureAwait(false);
                created += added;
                after = batch[batch.Count - 1].UserId;
                if (!await _Services.Database.NotificationFanouts.RecordProgressAsync(job.Id, holder, after, added, _Clock() + claimDuration, token).ConfigureAwait(false)) return -1;
                if (batch.Count < batchSize) break;
            }
            return created;
        }

        private async Task<int> ChangeAsync(NotificationFanout job, EventRecord e, List<string> recipients, bool series, string flags, string holder, TimeSpan claimDuration, int batchSize, CancellationToken token)
        {
            bool canceled = job.Type == NotificationTypeEnum.EventCanceled;
            string when = EventTimeZones.Friendly(e.StartUtc, e.TimeZone, e.IsAllDay);
            string title = canceled
                ? (series ? "Upcoming dates of " + e.Title + " were canceled" : e.Title + " was canceled")
                : series ? e.Title + ": upcoming dates changed (" + EventChangeSet.Describe(flags) + ")"
                : flags.Contains('T', StringComparison.Ordinal) ? e.Title + " moved to " + when : e.Title + ": " + EventChangeSet.Describe(flags);
            List<string> pending = recipients.Where(r => job.LastUserId == null || String.CompareOrdinal(r, job.LastUserId) > 0).ToList();
            int created = 0;
            for (int i = 0; i < pending.Count; i += batchSize)
            {
                token.ThrowIfCancellationRequested();
                List<string> batch = pending.Skip(i).Take(batchSize).ToList();
                int added;
                if (canceled)
                {
                    List<NotificationDraft> drafts = batch.Select(r => Draft(job.Type, e, r, title)).Select(d => { d.Body = e.CancelReason; return d; }).ToList();
                    added = await _Services.Notifications.CreateManyAsync(drafts, true, token).ConfigureAwait(false);
                }
                else
                {
                    HashSet<string> refreshed = await _Services.Database.Events.Records.RefreshUnreadNoticesAsync(NotificationTypeEnum.EventUpdated, e.Id, batch, title, _Clock(), token).ConfigureAwait(false);
                    List<NotificationDraft> drafts = batch.Where(r => !refreshed.Contains(r)).Select(r => Draft(job.Type, e, r, title)).ToList();
                    added = refreshed.Count + (drafts.Count == 0 ? 0 : await _Services.Notifications.CreateManyAsync(drafts, false, token).ConfigureAwait(false));
                }
                created += added;
                List<User> users = await _Services.Database.Users.ReadManyAsync(batch, token).ConfigureAwait(false);
                List<NotificationPreference> preferences = canceled ? new List<NotificationPreference>() : await _Services.Database.NotificationPreferences.EnumerateAsync(batch, token).ConfigureAwait(false);
                foreach (User user in users.Where(u => !u.IsDeleted && !u.IsBanned))
                {
                    if (!canceled && !NotificationPreferenceService.Effective(user.Id, preferences.Where(p => p.UserId == user.Id), NotificationCategoryEnum.Events).Email) continue;
                    string location = await LocationTextAsync(e, user, token).ConfigureAwait(false);
                    if (canceled)
                    {
                        await QueueAsync(EventCanceledEmail.TemplateKey, user, "event_canceled:" + job.SourceId + ":" + user.Id, false, token,
                            "eventTitle", e.Title, "startLocal", when, "reason", e.CancelReason ?? String.Empty, "eventLink", EventLink(e.Id)).ConfigureAwait(false);
                    }
                    else
                    {
                        await QueueAsync(EventUpdatedEmail.TemplateKey, user, "event_updated:" + job.SourceId + ":" + user.Id, true, token,
                            "eventTitle", e.Title, "changeSummary", (series ? "Upcoming dates of this event changed: " : "The ") + EventChangeSet.Describe(flags) + ".", "startLocal", when,
                            "locationText", location, "eventLink", EventLink(e.Id), "calendarLink", CalendarLink(e.Id)).ConfigureAwait(false);
                    }
                }
                if (!await _Services.Database.NotificationFanouts.RecordProgressAsync(job.Id, holder, batch[batch.Count - 1], added, _Clock() + claimDuration, token).ConfigureAwait(false)) return -1;
            }
            return created;
        }

        private async Task<int> ReminderAsync(NotificationFanout job, EventRecord e, string[] parts, string holder, TimeSpan claimDuration, int batchSize, CancellationToken token)
        {
            if (e.Status != EventStatusEnum.Published || e.ModerationState != ModerationStateEnum.None) return 0;
            int offset = parts.Length > 1 && Int32.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int o) ? o : 0;
            int largest = e.ReminderOffsetsMinutes.Count == 0 ? offset : e.ReminderOffsetsMinutes.Max();
            bool first = offset >= largest;
            List<EventRsvpStatusEnum> statuses = new List<EventRsvpStatusEnum> { EventRsvpStatusEnum.Going };
            if (_Services.Settings.Events.RemindInterested) statuses.Add(EventRsvpStatusEnum.Interested);
            if (first) statuses.Add(EventRsvpStatusEnum.Waitlisted);
            string starts = e.Title + " starts " + InText(e.StartUtc - _Clock());
            string when = EventTimeZones.Friendly(e.StartUtc, e.TimeZone, e.IsAllDay);
            string? after = job.LastUserId;
            int created = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                List<EventRsvp> batch = await _Services.Database.Events.Rsvps.ListRecipientsAsync(e.Id, statuses, after, batchSize, token).ConfigureAwait(false);
                if (batch.Count == 0) break;
                HashSet<string> allowed = await MayOpenAsync(e, batch.Select(r => r.UserId).ToList(), token).ConfigureAwait(false);
                List<EventRsvp> recipients = batch.Where(r => allowed.Contains(r.UserId)).ToList();
                List<NotificationDraft> drafts = recipients.Select(r => Draft(NotificationTypeEnum.EventReminder, e, r.UserId, r.Status == EventRsvpStatusEnum.Waitlisted ? "You are on the waitlist: " + starts : starts)).ToList();
                foreach (NotificationDraft d in drafts) d.GroupKey = null;
                int added = drafts.Count == 0 ? 0 : await _Services.Notifications.CreateManyAsync(drafts, false, token).ConfigureAwait(false);
                created += added;
                EventTelemetry.Count(EventTelemetry.Reminders, added, "outcome", "sent", "channel", "inapp");
                if (first)
                {
                    List<string> going = recipients.Where(r => r.Status != EventRsvpStatusEnum.Waitlisted).Select(r => r.UserId).ToList();
                    List<User> users = await _Services.Database.Users.ReadManyAsync(going, token).ConfigureAwait(false);
                    List<NotificationPreference> preferences = await _Services.Database.NotificationPreferences.EnumerateAsync(going, token).ConfigureAwait(false);
                    string joinNote = e.Format == EventFormatEnum.InPerson ? String.Empty : "The join link is available in the event from " +
                        EventTimeZones.Friendly(EventAccess.JoinUrlAvailableUtc(e, _Services.Settings.Events.JoinUrlRevealMinutes), e.TimeZone, false) + ".";
                    foreach (User user in users.Where(u => !u.IsDeleted && !u.IsBanned))
                    {
                        if (!NotificationPreferenceService.Effective(user.Id, preferences.Where(p => p.UserId == user.Id), NotificationCategoryEnum.Events).Email) continue;
                        await QueueAsync(EventReminderEmail.TemplateKey, user, "event_reminder:" + job.SourceId + ":" + user.Id, true, token,
                            "eventTitle", e.Title, "startLocal", when, "timeZone", e.TimeZone, "locationText", await LocationTextAsync(e, user, token).ConfigureAwait(false),
                            "joinNote", joinNote, "eventLink", EventLink(e.Id), "calendarLink", CalendarLink(e.Id)).ConfigureAwait(false);
                        EventTelemetry.Count(EventTelemetry.Reminders, 1, "outcome", "sent", "channel", "email");
                    }
                }
                after = batch[batch.Count - 1].UserId;
                if (!await _Services.Database.NotificationFanouts.RecordProgressAsync(job.Id, holder, after, added, _Clock() + claimDuration, token).ConfigureAwait(false)) return -1;
                if (batch.Count < batchSize) break;
            }
            return created;
        }

        private async Task EventManagersAsync(string template, string key, string[] tokens, CancellationToken token)
        {
            List<RoleAssignment> managers = await _Services.Database.RoleAssignments.EnumerateByRoleAsync(RoleEnum.EventManager, 10, token).ConfigureAwait(false);
            List<User> users = await _Services.Database.Users.ReadManyAsync(managers.Select(m => m.UserId).Distinct(StringComparer.Ordinal).ToList(), token).ConfigureAwait(false);
            foreach (User user in users.Where(u => !u.IsDeleted && !u.IsBanned && !String.IsNullOrWhiteSpace(u.Email)))
            {
                EmailRequest request = new EmailRequest { Template = template, To = user.Email!, UserId = user.Id, IdempotencyKey = key + ":" + user.Email!.Trim().ToLowerInvariant() };
                for (int i = 0; i + 1 < tokens.Length; i += 2) request.Tokens[tokens[i]] = tokens[i + 1] ?? String.Empty;
                request.Tokens["memberName"] = user.DisplayName;
                await _Services.Email.QueueAsync(request, token).ConfigureAwait(false);
            }
        }

        private async Task<List<string>> EventRecipientsAsync(string eventId, List<EventRsvpStatusEnum> statuses, CancellationToken token)
        {
            List<string> result = new List<string>();
            string? after = null;
            while (true)
            {
                List<EventRsvp> page = await _Services.Database.Events.Rsvps.ListRecipientsAsync(eventId, statuses, after, 1000, token).ConfigureAwait(false);
                result.AddRange(page.Select(r => r.UserId));
                if (page.Count < 1000) break;
                after = page[page.Count - 1].UserId;
            }
            return result;
        }

        private async Task<List<string>> SeriesRecipientsAsync(string recurrenceId, DateTime sinceUtc, bool canceled, CancellationToken token)
        {
            SortedSet<string> members = new SortedSet<string>(StringComparer.Ordinal);
            List<EventRsvpStatusEnum> statuses = new List<EventRsvpStatusEnum> { EventRsvpStatusEnum.Going, EventRsvpStatusEnum.Waitlisted, EventRsvpStatusEnum.Interested };
            foreach (EventRecord o in await _Services.Database.Events.Records.ListOccurrencesAsync(recurrenceId, 5000, token).ConfigureAwait(false))
            {
                bool hit = canceled ? o.Status == EventStatusEnum.Canceled && o.CanceledUtc != null && o.CanceledUtc.Value >= sinceUtc.AddSeconds(-1) : o.UpdatedUtc >= sinceUtc.AddSeconds(-1) && o.Status == EventStatusEnum.Published;
                if (!hit) continue;
                foreach (string userId in await EventRecipientsAsync(o.Id, statuses, token).ConfigureAwait(false)) members.Add(userId);
            }
            return members.ToList();
        }

        private async Task<HashSet<string>> MayOpenAsync(EventRecord e, List<string> userIds, CancellationToken token)
        {
            HashSet<string> allowed = new HashSet<string>(StringComparer.Ordinal);
            if (userIds.Count == 0) return allowed;
            AccessPolicy policy = _Services.Access.Current;
            DateTime now = _Clock();
            List<User> users = await _Services.Database.Users.ReadManyAsync(userIds, token).ConfigureAwait(false);
            List<Entitlement> entitlements = await _Services.Database.Entitlements.EnumerateByUsersAsync(userIds, token).ConfigureAwait(false);
            HashSet<string> members = new HashSet<string>(entitlements.Where(x => x.IsActiveAt(now)).Select(x => x.UserId), StringComparer.Ordinal);
            List<RoleAssignment> roles = await _Services.Database.RoleAssignments.EnumerateByUsersAsync(userIds, token).ConfigureAwait(false);
            HashSet<string> admins = new HashSet<string>(roles.Where(r => r.Role == RoleEnum.SystemAdmin || r.Role == RoleEnum.EventManager).Select(r => r.UserId), StringComparer.Ordinal);
            foreach (User user in users)
            {
                if (user.IsDeleted || user.IsBanned) continue;
                bool hasMembership = members.Contains(user.Id);
                bool staff = admins.Contains(user.Id) || e.HostUserIds.Contains(user.Id, StringComparer.Ordinal);
                if (!ContentAudienceRule.RecipientMayOpen(policy, e.Audience, staff, user.AgeAttestedUtc != null, hasMembership)) continue;
                if (!staff && !AccessDecider.Meets(AccessDecider.TierOf(false, true, user.AgeAttestedUtc != null, hasMembership), EventAccess.RsvpTier(e, policy))) continue;
                allowed.Add(user.Id);
            }
            return allowed;
        }

        private async Task<string> LocationTextAsync(EventRecord e, User user, CancellationToken token)
        {
            if (e.Format == EventFormatEnum.Online) return String.IsNullOrWhiteSpace(e.OnlinePlatformLabel) ? "Online" : e.OnlinePlatformLabel!;
            EventVenueSnapshot? venue = e.VenueId == null ? null : await _Services.Database.Events.Records.ReadVenueAsync(e.VenueId, token).ConfigureAwait(false);
            ContentActor viewer = new ContentActor { UserId = user.Id, User = user, AgeAttested = user.AgeAttestedUtc != null, HasMembership = true, Access = _Services.Access.Current };
            string text = EventViewBuilder.LocationText(EventViewBuilder.Location(e, venue, viewer));
            return text.Length == 0 ? "See the event for details" : text;
        }

        private NotificationDraft Draft(NotificationTypeEnum type, EventRecord e, string recipient, string title)
        {
            return new NotificationDraft
            {
                RecipientUserId = recipient,
                Type = type,
                TargetType = TargetTypeEnum.Event,
                TargetId = e.Id,
                TitleText = title.Length > 200 ? title.Substring(0, 200) : title,
                Path = "/events/" + e.Id,
                EventUtc = e.StartUtc,
                QueuePush = true
            };
        }

        private async Task QueueAsync(string template, User user, string idempotencyKey, bool optional, CancellationToken token, params string[] tokens)
        {
            if (String.IsNullOrWhiteSpace(user.Email)) return;
            EmailRequest request = new EmailRequest { Template = template, To = user.Email, UserId = user.Id, IdempotencyKey = idempotencyKey.Length > 300 ? idempotencyKey.Substring(0, 300) : idempotencyKey };
            request.Tokens["memberName"] = user.DisplayName;
            for (int i = 0; i + 1 < tokens.Length; i += 2) request.Tokens[tokens[i]] = tokens[i + 1] ?? String.Empty;
            if (optional)
            {
                string unsubscribe;
                try
                {
                    unsubscribe = _Services.FeedPostEmails.UnsubscribeUrl(_Services.FeedPostEmails.CreateUnsubscribeToken(user.Id, NotificationCategoryEnum.Events));
                    request.Headers["List-Unsubscribe"] = "<" + unsubscribe + ">";
                    request.Headers["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click";
                }
                catch (IntegrationNotConfiguredException)
                {
                    unsubscribe = _Services.Email.Link("/app/settings/notifications");
                }
                request.Tokens["unsubscribeLink"] = unsubscribe;
            }
            await _Services.Email.QueueAsync(request, token).ConfigureAwait(false);
        }

        private static string InText(TimeSpan span)
        {
            double minutes = Math.Max(0, Math.Round(span.TotalMinutes));
            if (minutes >= 2880) return "in " + Math.Round(minutes / 1440).ToString(CultureInfo.InvariantCulture) + " days";
            if (minutes >= 1380) return "tomorrow";
            if (minutes >= 90) return "in " + Math.Round(minutes / 60).ToString(CultureInfo.InvariantCulture) + " hours";
            if (minutes >= 55) return "in 1 hour";
            if (minutes < 1) return "now";
            return "in " + minutes.ToString(CultureInfo.InvariantCulture) + " minutes";
        }

        #endregion
    }
}
