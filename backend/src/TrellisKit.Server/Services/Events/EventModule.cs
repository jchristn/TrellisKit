namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// The Phase 4 services of events (occurrences and recurrence, RSVPs, reminders, proposals, calendar feeds) for one property (docs/PHASE4_CONTRACT.md section 4)
    /// and their wiring into the Phase 2 and Phase 3 services.
    /// </summary>
    public class EventModule
    {
        #region Public-Members

        /// <summary>
        /// Payload builders.
        /// </summary>
        public EventViewBuilder Views { get; }

        /// <summary>
        /// Create and edit validation.
        /// </summary>
        public EventEditor Editor { get; }

        /// <summary>
        /// Events (member, public, and staff routes).
        /// </summary>
        public EventService Catalog { get; }

        /// <summary>
        /// Recurring events.
        /// </summary>
        public RecurrenceService Recurrence { get; }

        /// <summary>
        /// RSVPs.
        /// </summary>
        public RsvpService Rsvps { get; }

        /// <summary>
        /// Notifications, fan-outs, and emails.
        /// </summary>
        public EventNotifier Notifier { get; }

        /// <summary>
        /// The reminder worker pass.
        /// </summary>
        public EventReminderService Reminders { get; }

        /// <summary>
        /// ICS and personal calendar feeds.
        /// </summary>
        public CalendarService Calendar { get; }

        /// <summary>
        /// Event categories and the property document decorator.
        /// </summary>
        public EventCategoryService Categories { get; }

        /// <summary>
        /// Member proposals.
        /// </summary>
        public EventProposalService Proposals { get; }

        /// <summary>
        /// The Event content target provider (comments, reactions, reports, moderation, search).
        /// </summary>
        public EventContentProvider Provider { get; }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private int _Attached = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the module (call <see cref="Attach"/> once the Phase 2 and Phase 3 services exist).
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public EventModule(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            Views = new EventViewBuilder(services);
            Editor = new EventEditor(services);
            Catalog = new EventService(services);
            Recurrence = new RecurrenceService(services);
            Rsvps = new RsvpService(services);
            Notifier = new EventNotifier(services);
            Reminders = new EventReminderService(services);
            Calendar = new CalendarService(services);
            Categories = new EventCategoryService(services);
            Proposals = new EventProposalService(services);
            Provider = new EventContentProvider(services);
            services.ContentTargets.Register(Provider);
            services.PropertyDecorators.Add(Categories.DecorateAsync);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Wire the module into the shared services (idempotent).
        /// </summary>
        public void Attach()
        {
            if (Interlocked.Exchange(ref _Attached, 1) != 0) return;
            Notifier.Register();
            _Services.Ai.Tools.Register(new TrellisKit.Server.Services.Ai.AssistantTool
            {
                Name = "findEvents",
                Label = "Finding events",
                CompletedLabel = "Found events",
                Description = "Find upcoming events the member may see. Arguments: query (text), category (key), startsAfter and startsBefore (ISO 8601 UTC), maxResults (1 to 10).",
                ParametersSchema = "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"},\"category\":{\"type\":\"string\"},\"startsAfter\":{\"type\":\"string\"},\"startsBefore\":{\"type\":\"string\"},\"maxResults\":{\"type\":\"integer\"}}}",
                WorksSignedOut = true,
                Handler = (caller, args, token) => Provider.FindEventsAsync(caller, args, token)
            });
            EventExportSections.Register(_Services);
            // A cover processed after its event was published reaches the public paths once it is Ready.
            _Services.Media.AddSettledHandler((media, token) => media.AttachedTargetType == TargetTypeEnum.Event && media.Kind == MediaKindEnum.Image
                ? Catalog.SyncCoversAsync(new[] { media.Id }, token)
                : Task.CompletedTask);
            Func<string, CancellationToken, Task>? closed = _Services.Accounts.AccountClosed;
            _Services.Accounts.AccountClosed = async (userId, token) =>
            {
                await EraseForAccountAsync(userId, token).ConfigureAwait(false);
                if (closed != null) await closed(userId, token).ConfigureAwait(false);
            };
            Func<string, CancellationToken, Task>? banned = _Services.AdminMembers.MemberBanned;
            _Services.AdminMembers.MemberBanned = async (userId, token) =>
            {
                await BannedAsync(userId, token).ConfigureAwait(false);
                if (banned != null) await banned(userId, token).ConfigureAwait(false);
            };
        }

        /// <summary>
        /// Replace the clock of every event service (tests).
        /// </summary>
        /// <param name="clock">Clock.</param>
        /// <exception cref="ArgumentNullException">The clock is null.</exception>
        public void SetClock(Func<DateTime> clock)
        {
            ArgumentNullException.ThrowIfNull(clock);
            Views.Clock = clock;
            Catalog.Clock = clock;
            Recurrence.Clock = clock;
            Rsvps.Clock = clock;
            Notifier.Clock = clock;
            Reminders.Clock = clock;
            Calendar.Clock = clock;
        }

        /// <summary>
        /// Account deletion (docs/PHASE4_CONTRACT.md section 10): RSVPs deleted (upcoming ones one by one so the
        /// waitlist promotion runs), the calendar feed deleted, host rows removed (events stay), pending proposals
        /// withdrawn.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>RSVPs deleted.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public async Task<int> EraseForAccountAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            int removed = await RemoveUpcomingRsvpsAsync(userId, token).ConfigureAwait(false);
            removed += await _Services.Database.Events.Rsvps.DeleteStartedForUserAsync(userId, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Database.Events.Feeds.DeleteForUserAsync(userId, token).ConfigureAwait(false);
            await _Services.Database.Events.Records.RemoveHostEverywhereAsync(userId, token).ConfigureAwait(false);
            EventListFilter filter = new EventListFilter { NowUtc = DateTime.UtcNow, When = EventWhenEnum.All, ProposedByUserId = userId, Statuses = new List<EventStatusEnum> { EventStatusEnum.Proposed }, OnlyUnmoderated = false };
            TrellisKit.Core.Enumeration.EnumerationRequest request = _Services.Enumeration.Prepare(new TrellisKit.Core.Requests.EnumerationQuery { MaxResults = 100 }, TrellisKit.Core.Enumeration.EventCollections.AdminProposals, true);
            foreach (EventRecord e in (await _Services.Database.Events.Records.EnumerateAsync(filter, request, token).ConfigureAwait(false)).Objects)
            {
                e.Status = EventStatusEnum.Withdrawn;
                await _Services.Database.Events.Records.UpdateAsync(e, false, DateTime.UtcNow, token).ConfigureAwait(false);
            }
            return removed;
        }

        /// <summary>
        /// A ban (docs/PHASE4_CONTRACT.md section 10): RSVPs on upcoming events removed (promotion runs) and the
        /// calendar feed disabled.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>RSVPs removed.</returns>
        public async Task<int> BannedAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            int removed = await RemoveUpcomingRsvpsAsync(userId, token).ConfigureAwait(false);
            await _Services.Database.Events.Feeds.DisableForUserAsync(userId, token).ConfigureAwait(false);
            return removed;
        }

        /// <summary>
        /// Add the module's background workers for this property.
        /// </summary>
        /// <param name="workers">Worker list of the property runtime.</param>
        /// <param name="token">Lifetime token (for signaled passes).</param>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        public void AddWorkers(List<IntervalWorker> workers, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(workers);
            workers.Add(new LeasedPassWorker(_Services.Database, "event_reminders", () => TimeSpan.FromSeconds(Math.Clamp(_Services.Settings.Events.ReminderIntervalSeconds, 15, 600)), TimeSpan.FromSeconds(20),
                ct => Reminders.PassAsync(ct), _Services.Logger));
            workers.Add(new LeasedPassWorker(_Services.Database, "event_recurrence", () => TimeSpan.FromSeconds(Math.Clamp(_Services.Settings.Events.RecurrenceIntervalSeconds, 60, 86400)), TimeSpan.FromMinutes(1),
                ct => Recurrence.HorizonPassAsync(ct), _Services.Logger));
            workers.Add(new LeasedPassWorker(_Services.Database, "event_rsvp_digest", () => TimeSpan.FromHours(1), TimeSpan.FromMinutes(3),
                ct => Notifier.StaffDigestPassAsync(ct), _Services.Logger));
        }

        #endregion

        #region Private-Methods

        private async Task<int> RemoveUpcomingRsvpsAsync(string userId, CancellationToken token)
        {
            int removed = 0;
            string? after = null;
            DateTime now = DateTime.UtcNow;
            List<string> upcoming = new List<string>();
            for (int page = 0; page < 100; page++)
            {
                List<EventRsvpRecord> rows = await _Services.Database.Events.Rsvps.ListForUserAsync(userId, after, 500, token).ConfigureAwait(false);
                upcoming.AddRange(rows.Where(r => r.EventStartUtc > now && r.EventStatus == EventStatusEnum.Published).Select(r => r.Rsvp.EventId));
                if (rows.Count < 500) break;
                after = rows[rows.Count - 1].Rsvp.Id;
            }
            foreach (string eventId in upcoming.Distinct(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                await Rsvps.RemoveForLifecycleAsync(eventId, userId, token).ConfigureAwait(false);
                removed++;
            }
            return removed;
        }

        #endregion
    }
}
