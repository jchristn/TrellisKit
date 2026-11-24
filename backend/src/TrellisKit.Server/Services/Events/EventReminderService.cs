namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;

    /// <summary>
    /// The EventReminderWorker pass (docs/PHASE4_CONTRACT.md section 4.4): claims due reminders of this property
    /// (oldest first, bounded per pass), skips those later than events.reminderGraceMinutes (never sent late) and those
    /// of events that are no longer published, and starts one idempotent fan-out job per reminder keyed
    /// (EventReminder, evt_x:offset:dueTicks), so a rescheduled reminder sends again while a retried pass never sends
    /// twice. Thread safety: one pass at a time per property (the worker holds a job lease).
    /// </summary>
    public class EventReminderService
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

        /// <summary>
        /// Reminders claimed per pass. Default 200.
        /// </summary>
        public int BatchSize
        {
            get { return _BatchSize; }
            set { _BatchSize = Math.Clamp(value, 1, 1000); }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;
        private int _BatchSize = 200;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public EventReminderService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// One pass.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Reminders handled (sent or skipped).</returns>
        public async Task<int> PassAsync(CancellationToken token = default)
        {
            DateTime now = _Clock();
            DateTime late = now.AddMinutes(-_Services.Settings.Events.ReminderGraceMinutes);
            List<EventReminder> due = await _Services.Database.Events.Reminders.ListDueAsync(now, _BatchSize, token).ConfigureAwait(false);
            int handled = 0;
            Dictionary<string, EventRecord?> events = new Dictionary<string, EventRecord?>(StringComparer.Ordinal);
            foreach (EventReminder r in due)
            {
                token.ThrowIfCancellationRequested();
                if (!events.TryGetValue(r.EventId, out EventRecord? e))
                {
                    e = await _Services.Database.Events.Records.ReadAsync(r.EventId, token).ConfigureAwait(false);
                    events[r.EventId] = e;
                }
                bool live = e != null && e.Status == EventStatusEnum.Published && e.ModerationState == ModerationStateEnum.None && e.StartUtc > now;
                if (!live || r.DueUtc < late)
                {
                    if (await _Services.Database.Events.Reminders.MarkSkippedAsync(r.Id, now, token).ConfigureAwait(false))
                    {
                        handled++;
                        EventTelemetry.Count(EventTelemetry.Reminders, 1, "outcome", "skipped_late", "channel", "none");
                    }
                    continue;
                }
                string source = r.EventId + ":" + r.OffsetMinutes.ToString(CultureInfo.InvariantCulture) + ":" + r.DueUtc.Ticks.ToString(CultureInfo.InvariantCulture);
                await _Services.Notifications.StartFanoutAsync(NotificationTypeEnum.EventReminder, source, token).ConfigureAwait(false);
                if (await _Services.Database.Events.Reminders.MarkSentAsync(r.Id, now, token).ConfigureAwait(false)) handled++;
            }
            return handled;
        }

        #endregion
    }
}
