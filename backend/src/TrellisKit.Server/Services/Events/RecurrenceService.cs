namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Recurring events (resolved decision 11). A recurrence row holds the rule and the local start of the series; a
    /// hidden series template row (status Template) holds the fields new occurrences copy; occurrences are ordinary
    /// events rows (each with its own id, RSVPs, capacity, reminders, and comments) linked by recurrenceid,
    /// originalstartutc, and occurrenceindex, materialized up to events.recurrenceHorizonDays ahead and at most
    /// events.maxOccurrences per recurrence. Occurrences are matched to the rule by their original local date, so a
    /// time change keeps every RSVP on its occurrence. Edit scopes: One (the occurrence becomes an exception),
    /// Following (the rule is split before the occurrence), All (every not-yet-started occurrence that is not an
    /// exception). Thread safety: stateless apart from the shared services.
    /// </summary>
    public class RecurrenceService
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

        private static readonly string[] _Names = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        private static readonly HashSet<string> _TimeFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "startLocal", "endLocal", "timeZone", "isAllDay" };
        private static readonly HashSet<string> _NotCopied = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "startLocal", "endLocal", "timeZone", "isAllDay", "recurrence", "scope", "notifyAttendees", "slug", "rsvpOpensUtc", "rsvpClosesUtc", "labels", "tags" };
        private readonly ApplicationServices _Services;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public RecurrenceService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The short weekday name (Mon to Sun).
        /// </summary>
        /// <param name="day">Day.</param>
        /// <returns>Name.</returns>
        public static string WeekdayName(DayOfWeek day)
        {
            return _Names[(int)day];
        }

        /// <summary>
        /// Parse a weekday name (Mon, Monday, MO), or null.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Day or null.</returns>
        public static DayOfWeek? ParseWeekday(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            string t = text.Trim();
            for (int i = 0; i < 7; i++)
            {
                DayOfWeek day = (DayOfWeek)i;
                if (String.Equals(t, _Names[i], StringComparison.OrdinalIgnoreCase) || String.Equals(t, day.ToString(), StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(t, _Names[i].Substring(0, 2), StringComparison.OrdinalIgnoreCase)) return day;
            }
            return null;
        }

        /// <summary>
        /// The rule of a stored recurrence.
        /// </summary>
        /// <param name="r">Recurrence.</param>
        /// <returns>Rule.</returns>
        public static EventRecurrenceRule RuleOf(EventRecurrence r)
        {
            ArgumentNullException.ThrowIfNull(r);
            DateTime? until = null;
            if (!String.IsNullOrEmpty(r.UntilLocalDate) && DateTime.TryParseExact(r.UntilLocalDate, EventTimeZones.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime u)) until = u.Date;
            return new EventRecurrenceRule
            {
                Frequency = r.Frequency,
                Interval = Math.Max(1, r.Interval),
                Weekdays = new List<DayOfWeek>(r.Weekdays),
                MonthDay = r.MonthDay,
                SetPosition = r.SetPosition,
                Count = r.Count,
                UntilDate = until
            };
        }

        /// <summary>
        /// Validate a recurrence request (field errors go to errors with the prefix recurrence.).
        /// </summary>
        /// <param name="req">Request.</param>
        /// <param name="startLocal">Local start of the first occurrence.</param>
        /// <param name="settings">Event settings.</param>
        /// <param name="errors">Field errors.</param>
        /// <returns>Rule, or null when invalid.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static EventRecurrenceRule? Parse(EventRecurrenceRequest req, DateTime startLocal, EventSettings settings, Dictionary<string, string> errors)
        {
            ArgumentNullException.ThrowIfNull(req);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(errors);
            int before = errors.Count;
            if (req.Frequency == null) errors["recurrence.frequency"] = "Use Daily, Weekly, Monthly, or Yearly.";
            int interval = req.Interval ?? 1;
            if (interval < 1 || interval > 99) errors["recurrence.interval"] = "Use 1 to 99.";
            List<DayOfWeek> days = new List<DayOfWeek>();
            foreach (string w in req.Weekdays ?? new List<string>())
            {
                DayOfWeek? day = ParseWeekday(w);
                if (day == null) errors["recurrence.weekdays"] = "Use Mon, Tue, Wed, Thu, Fri, Sat, or Sun.";
                else if (!days.Contains(day.Value)) days.Add(day.Value);
            }
            EventRecurrenceFrequencyEnum frequency = req.Frequency ?? EventRecurrenceFrequencyEnum.Weekly;
            if (frequency == EventRecurrenceFrequencyEnum.Monthly)
            {
                if (req.SetPosition != null)
                {
                    if (req.SetPosition.Value != -1 && (req.SetPosition.Value < 1 || req.SetPosition.Value > 4)) errors["recurrence.setPosition"] = "Use 1 to 4, or -1 for the last.";
                    if (days.Count != 1) errors["recurrence.weekdays"] = "With setPosition, give exactly one weekday.";
                    if (req.MonthDay != null) errors["recurrence.monthDay"] = "Use monthDay or setPosition, not both.";
                }
                else if (req.MonthDay != null && req.MonthDay.Value != -1 && (req.MonthDay.Value < 1 || req.MonthDay.Value > 31)) errors["recurrence.monthDay"] = "Use 1 to 31, or -1 for the last day.";
                else if (days.Count > 0) errors["recurrence.weekdays"] = "Monthly rules take weekdays only with setPosition.";
            }
            else
            {
                if (req.SetPosition != null) errors["recurrence.setPosition"] = "setPosition is for Monthly rules.";
                if (req.MonthDay != null) errors["recurrence.monthDay"] = "monthDay is for Monthly rules.";
                if (frequency != EventRecurrenceFrequencyEnum.Weekly && days.Count > 0) errors["recurrence.weekdays"] = "weekdays are for Weekly and Monthly rules.";
            }
            if (req.Count != null && req.UntilDate != null) errors["recurrence.count"] = "Use count or untilDate, not both.";
            if (req.Count != null && (req.Count.Value < 1 || req.Count.Value > settings.MaxOccurrences)) errors["recurrence.count"] = "Use 1 to " + settings.MaxOccurrences.ToString(CultureInfo.InvariantCulture) + ".";
            DateTime? until = null;
            if (!String.IsNullOrWhiteSpace(req.UntilDate))
            {
                if (!DateTime.TryParseExact(req.UntilDate.Trim(), EventTimeZones.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime u)) errors["recurrence.untilDate"] = "Use yyyy-MM-dd.";
                else if (u.Date < startLocal.Date) errors["recurrence.untilDate"] = "The until date is before the first occurrence.";
                else until = u.Date;
            }
            if (errors.Count > before) return null;
            if (frequency == EventRecurrenceFrequencyEnum.Weekly && days.Count == 0) days.Add(startLocal.DayOfWeek);
            return new EventRecurrenceRule
            {
                Frequency = frequency,
                Interval = interval,
                Weekdays = days.OrderBy(d => ((int)d + 6) % 7).ToList(),
                MonthDay = frequency == EventRecurrenceFrequencyEnum.Monthly && req.SetPosition == null ? req.MonthDay ?? startLocal.Day : null,
                SetPosition = frequency == EventRecurrenceFrequencyEnum.Monthly ? req.SetPosition : null,
                Count = req.Count,
                UntilDate = until
            };
        }

        /// <summary>
        /// Create a recurring event: the recurrence, its series template, and the occurrences up to the horizon.
        /// </summary>
        /// <param name="actor">Creator.</param>
        /// <param name="first">Validated first occurrence (its fields become the series fields).</param>
        /// <param name="rule">Rule.</param>
        /// <param name="startLocal">Local start of the first occurrence.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The first occurrence.</returns>
        /// <exception cref="ApiErrorException">400 InvalidSchedule when the first occurrence does not fall on the rule.</exception>
        public async Task<EventRecord> CreateSeriesAsync(ContentActor actor, EventRecord first, EventRecurrenceRule rule, DateTime startLocal, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(first);
            ArgumentNullException.ThrowIfNull(rule);
            if (!RecurrenceExpander.Matches(rule, startLocal.Date, startLocal.Date))
                throw EventTimeZones.Invalid("recurrence", "The first occurrence (" + startLocal.ToString(EventTimeZones.DateFormat, CultureInfo.InvariantCulture) + ") must fall on the rule: " + RecurrenceSummary.Describe(rule, startLocal) + ".");
            DateTime now = _Clock();
            EventRecurrence r = new EventRecurrence
            {
                Id = IdGenerator.Generate(EventIdPrefixes.Recurrence),
                TimeZone = first.TimeZone,
                StartLocal = EventTimeZones.Format(startLocal),
                DurationMinutes = (int)Math.Max(1, Math.Round((first.EndUtc - first.StartUtc).TotalMinutes)),
                IsAllDay = first.IsAllDay,
                CreatedByUserId = actor.UserId,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            SetRule(r, rule);
            EventRecord template = first.Clone();
            template.Id = IdGenerator.Generate(EventIdPrefixes.Event);
            template.Status = EventStatusEnum.Template;
            template.Slug = "series-" + r.Id.ToLowerInvariant().Replace('_', '-');
            template.RecurrenceId = r.Id;
            r.TemplateEventId = template.Id;
            await _Services.Database.Events.Records.CreateManyAsync(new List<EventRecord> { template }, now, token).ConfigureAwait(false);
            await _Services.Database.Events.Recurrences.CreateAsync(r, token).ConfigureAwait(false);
            await MaterializeAsync(r, first.Id, token).ConfigureAwait(false);
            List<EventRecord> occurrences = await _Services.Database.Events.Records.ListOccurrencesAsync(r.Id, 2, token).ConfigureAwait(false);
            return occurrences.FirstOrDefault() ?? template;
        }

        /// <summary>
        /// Create the missing occurrences of a recurrence up to the horizon and the occurrence cap.
        /// </summary>
        /// <param name="r">Recurrence.</param>
        /// <param name="firstId">Identifier to use for occurrence 0 when it is created, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Occurrences created.</returns>
        public async Task<int> MaterializeAsync(EventRecurrence r, string? firstId = null, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(r);
            DateTime now = _Clock();
            EventSettings s = _Services.Settings.Events;
            DateTime horizon = now.AddDays(s.RecurrenceHorizonDays);
            EventRecord? template = r.TemplateEventId == null ? null : await ReadTemplateAsync(r.TemplateEventId, token).ConfigureAwait(false);
            TimeZoneInfo? zone = EventTimeZones.Find(r.TimeZone);
            if (template == null || zone == null || template.CanceledUtc != null || !EventTimeZones.TryParseLocal(r.StartLocal, out DateTime anchor))
            {
                r.MaterializedThroughUtc = horizon;
                r.UpdatedUtc = now;
                await _Services.Database.Events.Recurrences.UpdateAsync(r, token).ConfigureAwait(false);
                return 0;
            }
            List<EventOccurrenceSlot> slots = RecurrenceExpander.Expand(RuleOf(r), anchor, zone, r.DurationMinutes, horizon, s.MaxOccurrences);
            List<EventRecord> existing = await _Services.Database.Events.Records.ListOccurrencesAsync(r.Id, 5000, token).ConfigureAwait(false);
            List<EventRecurrenceException> exceptions = await _Services.Database.Events.Recurrences.ListExceptionsAsync(r.Id, token).ConfigureAwait(false);
            HashSet<DateTime> taken = new HashSet<DateTime>(existing.Select(o => OriginalDate(o, zone)));
            foreach (EventRecurrenceException x in exceptions) taken.Add(EventTimeZones.ToLocal(x.OriginalStartUtc, zone).Date);
            List<EventRecord> created = new List<EventRecord>();
            List<string> slugs = new List<string>();
            foreach (EventOccurrenceSlot slot in slots)
            {
                if (taken.Contains(slot.StartLocal.Date)) continue;
                EventRecord o = FromTemplate(template, r, slot, slot.Index == 0 ? firstId : null, now);
                created.Add(o);
                slugs.Add(o.Slug);
            }
            if (created.Count > 0)
            {
                HashSet<string> used = await _Services.Database.Events.Records.TakenSlugsAsync(slugs, token).ConfigureAwait(false);
                foreach (EventRecord o in created.Where(c => used.Contains(c.Slug.ToLowerInvariant()))) o.Slug = o.Slug + "-" + o.Id.Substring(o.Id.Length - 6).ToLowerInvariant();
                foreach (List<EventRecord> batch in created.Chunk(50).Select(c => c.ToList()))
                    await _Services.Database.Events.Records.CreateManyAsync(batch, now, token).ConfigureAwait(false);
                foreach (EventRecord o in created)
                {
                    await _Services.Metadata.SnapshotAsync(TargetTypeEnum.Event, template.Id, TargetTypeEnum.Event, o.Id, token).ConfigureAwait(false);
                    await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Event, o.Id, token).ConfigureAwait(false);
                }
                EventTelemetry.Count(EventTelemetry.Occurrences, created.Count, "action", "materialize");
            }
            r.MaterializedCount = existing.Count + created.Count;
            r.MaterializedThroughUtc = horizon;
            r.UpdatedUtc = now;
            await _Services.Database.Events.Recurrences.UpdateAsync(r, token).ConfigureAwait(false);
            return created.Count;
        }

        /// <summary>
        /// One pass of the recurrence worker: extend the horizon of every recurrence that is behind it.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Occurrences created.</returns>
        public async Task<int> HorizonPassAsync(CancellationToken token = default)
        {
            DateTime now = _Clock();
            DateTime target = now.AddDays(_Services.Settings.Events.RecurrenceHorizonDays).AddHours(-12);
            int created = 0;
            for (int round = 0; round < 10; round++)
            {
                List<EventRecurrence> behind = await _Services.Database.Events.Recurrences.ListBehindHorizonAsync(target, 50, token).ConfigureAwait(false);
                if (behind.Count == 0) break;
                foreach (EventRecurrence r in behind)
                {
                    token.ThrowIfCancellationRequested();
                    created += await MaterializeAsync(r, null, token).ConfigureAwait(false);
                }
                if (behind.Count < 50) break;
            }
            return created;
        }

        /// <summary>
        /// Edit a series (scope Following or All) from one of its occurrences.
        /// </summary>
        /// <param name="actor">Caller (event staff, or a host for the fields hosts may edit).</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="target">The occurrence the edit was made from.</param>
        /// <param name="req">Request.</param>
        /// <param name="present">Present fields.</param>
        /// <param name="scope">Following or All.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The target occurrence afterwards.</returns>
        /// <exception cref="ApiErrorException">400 InvalidSchedule, 404, 409.</exception>
        public async Task<EventRecord> EditSeriesAsync(ContentActor actor, RequestInfo info, EventRecord target, EventRequest req, HashSet<string> present, EventEditScopeEnum scope, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(req);
            ArgumentNullException.ThrowIfNull(present);
            DateTime now = _Clock();
            EventRecurrence r = await RequireRecurrenceAsync(target, token).ConfigureAwait(false);
            EventRecord template = await ReadTemplateAsync(r.TemplateEventId!, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Event");
            if (scope == EventEditScopeEnum.Following && (target.OccurrenceIndex ?? 0) == 0) scope = EventEditScopeEnum.All;
            if (scope == EventEditScopeEnum.Following && target.StartUtc <= now) throw new ApiErrorException(409, "RsvpClosed", "This occurrence already started; edit a later one with scope Following.");
            TimeZoneInfo oldZone = EventTimeZones.Find(r.TimeZone) ?? TimeZoneInfo.Utc;
            if (!EventTimeZones.TryParseLocal(r.StartLocal, out DateTime oldAnchor)) oldAnchor = EventTimeZones.ToLocal(template.StartUtc, oldZone);
            bool timeChange = present.Any(_TimeFields.Contains);
            bool ruleChange = present.Contains("recurrence") && req.Recurrence != null;

            // Apply the non-time fields to the template; the template's result carries the series values.
            HashSet<string> fieldPresent = new HashSet<string>(present.Where(p => !_TimeFields.Contains(p) && p != "recurrence" && p != "scope"), StringComparer.OrdinalIgnoreCase);
            EventEditResult templateEdit = await _Services.Events.Editor.ApplyAsync(actor, template, req, fieldPresent, false, token).ConfigureAwait(false);
            EventRecord nextTemplate = templateEdit.Record;

            // Time: the target's new local start and end give the new time of day and duration.
            TimeZoneInfo zone = timeChange && !String.IsNullOrWhiteSpace(req.TimeZone) ? EventTimeZones.Require(req.TimeZone) : oldZone;
            DateTime targetOriginal = target.OriginalStartUtc != null ? EventTimeZones.ToLocal(target.OriginalStartUtc.Value, oldZone) : EventTimeZones.ToLocal(target.StartUtc, oldZone);
            DateTime newStart = EventTimeZones.ToLocal(target.StartUtc, oldZone);
            DateTime newEnd = EventTimeZones.ToLocal(target.EndUtc, oldZone);
            bool allDay = req.IsAllDay ?? r.IsAllDay;
            if (timeChange)
            {
                if (present.Contains("startLocal") && !EventTimeZones.TryParseLocal(req.StartLocal, out newStart)) throw EventTimeZones.Invalid("startLocal", "Use yyyy-MM-ddTHH:mm in the event's time zone.");
                if (present.Contains("endLocal"))
                {
                    if (!EventTimeZones.TryParseLocal(req.EndLocal, out newEnd)) throw EventTimeZones.Invalid("endLocal", "Use yyyy-MM-ddTHH:mm in the event's time zone.");
                }
                else if (present.Contains("startLocal")) newEnd = newStart + (EventTimeZones.ToLocal(target.EndUtc, oldZone) - EventTimeZones.ToLocal(target.StartUtc, oldZone));
                if (allDay) newEnd = newEnd.Date.AddDays(present.Contains("endLocal") ? 1 : 0);
                if (newEnd <= newStart) throw EventTimeZones.Invalid("endLocal", "The end must be after the start.");
                if ((newEnd - newStart).TotalDays > _Services.Settings.Events.MaxDurationDays) throw EventTimeZones.Invalid("endLocal", "An occurrence lasts at most " + _Services.Settings.Events.MaxDurationDays + " days.");
                if (!allDay) EventTimeZones.ToUtcStrict(newStart, zone, "startLocal");
            }
            int duration = (int)Math.Max(1, Math.Round((newEnd - newStart).TotalMinutes));
            Dictionary<string, string> errors = new Dictionary<string, string>();
            EventRecurrenceRule rule = ruleChange ? RecurrenceService.Parse(req.Recurrence!, newStart, _Services.Settings.Events, errors) ?? new EventRecurrenceRule() : RuleOf(r);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            int dateShift = (newStart.Date - targetOriginal.Date).Days;

            EventRecurrence series = r;
            DateTime anchor;
            if (scope == EventEditScopeEnum.All)
            {
                if (dateShift != 0 && !ruleChange) throw EventTimeZones.Invalid("startLocal", "To move every occurrence to other days, send a new recurrence rule, or edit with scope Following.");
                DateTime anchorDate = oldAnchor.Date.AddDays(dateShift);
                if (ruleChange) anchorDate = RecurrenceExpander.FirstOnOrAfter(rule, anchorDate) ?? anchorDate;
                anchor = anchorDate + (timeChange ? newStart.TimeOfDay : oldAnchor.TimeOfDay);
                if (!timeChange) duration = r.DurationMinutes;
            }
            else
            {
                anchor = newStart.Date + (timeChange ? newStart.TimeOfDay : oldAnchor.TimeOfDay);
                if (!timeChange) duration = r.DurationMinutes;
                if (!RecurrenceExpander.Matches(rule, anchor.Date, anchor.Date)) throw EventTimeZones.Invalid("recurrence", "This occurrence must fall on the rule: " + RecurrenceSummary.Describe(rule, anchor) + ".");
                int index = target.OccurrenceIndex ?? 0;
                if (!ruleChange && r.Count != null) rule.Count = Math.Max(1, r.Count.Value - index);
                series = new EventRecurrence
                {
                    Id = IdGenerator.Generate(EventIdPrefixes.Recurrence),
                    CreatedByUserId = actor.UserId,
                    CreatedUtc = now,
                    UpdatedUtc = now
                };
                EventRecord splitTemplate = nextTemplate.Clone();
                splitTemplate.Id = IdGenerator.Generate(EventIdPrefixes.Event);
                splitTemplate.Slug = "series-" + series.Id.ToLowerInvariant().Replace('_', '-');
                splitTemplate.RecurrenceId = series.Id;
                series.TemplateEventId = splitTemplate.Id;
                nextTemplate = splitTemplate;
                // End the old rule before this occurrence.
                r.Count = index;
                r.UntilLocalDate = null;
                r.IcsSequence++;
                r.UpdatedUtc = now;
            }
            series.TimeZone = zone.Id;
            series.StartLocal = EventTimeZones.Format(anchor);
            series.DurationMinutes = duration;
            series.IsAllDay = allDay;
            SetRule(series, rule);
            series.IcsSequence = series == r ? r.IcsSequence + (timeChange || ruleChange ? 1 : 0) : 0;
            nextTemplate.TimeZone = zone.Id;
            nextTemplate.IsAllDay = allDay;
            nextTemplate.StartUtc = EventTimeZones.ToUtcLenient(anchor, zone);
            nextTemplate.EndUtc = EventTimeZones.ToUtcLenient(anchor.AddMinutes(duration), zone);

            // The occurrences this edit applies to.
            List<EventRecord> occurrences = await _Services.Database.Events.Records.ListOccurrencesAsync(r.Id, 5000, token).ConfigureAwait(false);
            List<EventRecord> affected = occurrences.Where(o => o.StartUtc > now && (scope == EventEditScopeEnum.All || (o.OccurrenceIndex ?? 0) >= (target.OccurrenceIndex ?? 0))).ToList();
            if (series != r)
            {
                await _Services.Database.Events.Records.CreateManyAsync(new List<EventRecord> { nextTemplate }, now, token).ConfigureAwait(false);
                await _Services.Metadata.SnapshotAsync(TargetTypeEnum.Event, template.Id, TargetTypeEnum.Event, nextTemplate.Id, token).ConfigureAwait(false);
                await _Services.Database.Events.Recurrences.CreateAsync(series, token).ConfigureAwait(false);
                await _Services.Database.Events.Recurrences.MoveAsync(r.Id, series.Id, affected.Select(o => o.Id), target.OriginalStartUtc ?? target.StartUtc, token).ConfigureAwait(false);
                await _Services.Database.Events.Recurrences.UpdateAsync(r, token).ConfigureAwait(false);
                foreach (EventRecord o in affected) o.RecurrenceId = series.Id;
            }
            else
            {
                await _Services.Database.Events.Records.UpdateAsync(nextTemplate, templateEdit.ChildrenChanged, now, token).ConfigureAwait(false);
            }
            if (present.Contains("labels") || present.Contains("tags"))
                await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Event, nextTemplate.Id, req.Labels, req.Tags, token).ConfigureAwait(false);

            // Regenerate (time or rule change) or copy the fields to each affected occurrence.
            EventChangeSet changes = new EventChangeSet();
            changes.Merge(templateEdit.Changes);
            if (timeChange || ruleChange) changes.Time = true;
            List<EventRecord> changed = new List<EventRecord>();
            List<EventRecord> removed = new List<EventRecord>();
            if (timeChange || ruleChange || scope == EventEditScopeEnum.Following)
            {
                List<EventOccurrenceSlot> slots = RecurrenceExpander.Expand(rule, anchor, zone, duration, now.AddDays(_Services.Settings.Events.RecurrenceHorizonDays), _Services.Settings.Events.MaxOccurrences);
                Dictionary<DateTime, EventRecord> byDate = new Dictionary<DateTime, EventRecord>();
                foreach (EventRecord o in affected) byDate[OriginalDate(o, oldZone).AddDays(dateShift)] = o;
                HashSet<DateTime> onRule = new HashSet<DateTime>();
                foreach (EventOccurrenceSlot slot in slots)
                {
                    onRule.Add(slot.StartLocal.Date);
                    if (!byDate.TryGetValue(slot.StartLocal.Date, out EventRecord? o)) continue;
                    if (o.IsException && !String.Equals(o.Id, target.Id, StringComparison.Ordinal)) continue;
                    EventRecord next = CopyFields(nextTemplate, o, fieldPresent);
                    next.StartUtc = slot.StartUtc;
                    next.EndUtc = slot.EndUtc;
                    next.OriginalStartUtc = slot.StartUtc;
                    next.OccurrenceIndex = slot.Index;
                    next.TimeZone = zone.Id;
                    next.IsAllDay = allDay;
                    if (next.StartUtc != o.StartUtc || next.EndUtc != o.EndUtc || templateEdit.Changes.Material) next.IcsSequence = o.IcsSequence + 1;
                    changed.Add(next);
                }
                foreach (EventRecord o in affected)
                {
                    if (o.IsException || o.Status == EventStatusEnum.Canceled) continue;
                    if (onRule.Contains(OriginalDate(o, oldZone).AddDays(dateShift))) continue;
                    EventRecord gone = o.Clone();
                    gone.Status = EventStatusEnum.Canceled;
                    gone.CanceledUtc = now;
                    gone.CancelReason = "This date was removed from the series.";
                    gone.CanceledByUserId = actor.UserId;
                    gone.IsException = true;
                    gone.IcsSequence = o.IcsSequence + 1;
                    removed.Add(gone);
                }
            }
            else
            {
                foreach (EventRecord o in affected)
                {
                    if (o.IsException && !String.Equals(o.Id, target.Id, StringComparison.Ordinal)) continue;
                    EventRecord next = CopyFields(nextTemplate, o, fieldPresent);
                    if (templateEdit.Changes.Material) next.IcsSequence = o.IcsSequence + 1;
                    changed.Add(next);
                }
            }
            foreach (EventRecord o in changed)
            {
                await _Services.Database.Events.Records.UpdateAsync(o, templateEdit.ChildrenChanged, now, token).ConfigureAwait(false);
                if (present.Contains("labels") || present.Contains("tags")) await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Event, o.Id, req.Labels, req.Tags, token).ConfigureAwait(false);
                if (templateEdit.Changes.MoreSpace) await _Services.Events.Rsvps.PromoteAfterChangeAsync(o, token).ConfigureAwait(false);
                await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Event, o.Id, token).ConfigureAwait(false);
            }
            foreach (EventRecord o in removed)
            {
                await _Services.Database.Events.Records.UpdateAsync(o, false, now, token).ConfigureAwait(false);
                await _Services.Database.Events.Reminders.SkipPendingAsync(o.Id, now, token).ConfigureAwait(false);
                await UpsertExceptionAsync(series.Id, o, EventRecurrenceExceptionKindEnum.Canceled, token).ConfigureAwait(false);
                await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Event, o.Id, token).ConfigureAwait(false);
            }
            if (changed.Count > 0) EventTelemetry.Count(EventTelemetry.Occurrences, changed.Count, "action", "regenerate");
            if (removed.Count > 0) EventTelemetry.Count(EventTelemetry.Occurrences, removed.Count, "action", "cancel");
            EventRecurrence? stored = await _Services.Database.Events.Recurrences.ReadAsync(series.Id, token).ConfigureAwait(false);
            if (stored != null)
            {
                SetRule(stored, rule);
                stored.TimeZone = series.TimeZone;
                stored.StartLocal = series.StartLocal;
                stored.DurationMinutes = series.DurationMinutes;
                stored.IsAllDay = series.IsAllDay;
                stored.IcsSequence = series.IcsSequence;
                stored.TemplateEventId = series.TemplateEventId;
                stored.UpdatedUtc = now;
                await _Services.Database.Events.Recurrences.UpdateAsync(stored, token).ConfigureAwait(false);
                await MaterializeAsync(stored, null, token).ConfigureAwait(false);
            }
            bool notify = req.NotifyAttendees ?? true;
            if (notify && changes.Material && changed.Count > 0)
                await _Services.Events.Notifier.StartSeriesAsync(NotificationTypeEnum.EventUpdated, series.Id, target.Id, now, changes.Flags, token).ConfigureAwait(false);
            if (removed.Count > 0)
                await _Services.Events.Notifier.StartSeriesAsync(NotificationTypeEnum.EventCanceled, series.Id, target.Id, now, String.Empty, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "event.update", "Event", target.Id, "scope " + scope + "; " + changed.Count + " occurrences changed, " + removed.Count + " removed", token).ConfigureAwait(false);
            return await _Services.Database.Events.Records.ReadAsync(target.Id, token).ConfigureAwait(false) ?? target;
        }

        /// <summary>
        /// Record that an occurrence was edited or canceled on its own.
        /// </summary>
        /// <param name="recurrenceId">Recurrence.</param>
        /// <param name="occurrence">Occurrence.</param>
        /// <param name="kind">Kind.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public Task UpsertExceptionAsync(string recurrenceId, EventRecord occurrence, EventRecurrenceExceptionKindEnum kind, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(occurrence);
            return _Services.Database.Events.Recurrences.UpsertExceptionAsync(new EventRecurrenceException
            {
                Id = IdGenerator.Generate(EventIdPrefixes.RecurrenceException),
                RecurrenceId = recurrenceId,
                OriginalStartUtc = occurrence.OriginalStartUtc ?? occurrence.StartUtc,
                Kind = kind,
                EventId = kind == EventRecurrenceExceptionKindEnum.Deleted ? null : occurrence.Id,
                CreatedUtc = _Clock()
            }, token);
        }

        /// <summary>
        /// The occurrences a cancellation of a series applies to (Following: from the occurrence on, and the rule ends
        /// before it; All: every not-yet-started occurrence, and the template stops materializing).
        /// </summary>
        /// <param name="target">Occurrence the cancellation was made from.</param>
        /// <param name="scope">Following or All.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Occurrences to cancel (not started, not already canceled).</returns>
        public async Task<List<EventRecord>> SeriesCancelTargetsAsync(EventRecord target, EventEditScopeEnum scope, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            DateTime now = _Clock();
            EventRecurrence r = await RequireRecurrenceAsync(target, token).ConfigureAwait(false);
            List<EventRecord> occurrences = await _Services.Database.Events.Records.ListOccurrencesAsync(r.Id, 5000, token).ConfigureAwait(false);
            int from = target.OccurrenceIndex ?? 0;
            List<EventRecord> targets = occurrences.Where(o => o.StartUtc > now && o.Status != EventStatusEnum.Canceled && (scope == EventEditScopeEnum.All || (o.OccurrenceIndex ?? 0) >= from)).ToList();
            if (scope == EventEditScopeEnum.All || from == 0)
            {
                EventRecord? template = r.TemplateEventId == null ? null : await ReadTemplateAsync(r.TemplateEventId, token).ConfigureAwait(false);
                if (template != null)
                {
                    template.CanceledUtc = now;
                    await _Services.Database.Events.Records.UpdateAsync(template, false, now, token).ConfigureAwait(false);
                }
            }
            else
            {
                r.Count = from;
                r.UntilLocalDate = null;
                r.IcsSequence++;
                r.UpdatedUtc = now;
                await _Services.Database.Events.Recurrences.UpdateAsync(r, token).ConfigureAwait(false);
            }
            return targets;
        }

        /// <summary>
        /// Publish the draft occurrences of a series that have not started, and mark the series published so later
        /// occurrences are created published.
        /// </summary>
        /// <param name="target">An occurrence of the series.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Occurrences published.</returns>
        public async Task<List<EventRecord>> PublishSeriesAsync(EventRecord target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            DateTime now = _Clock();
            EventRecurrence r = await RequireRecurrenceAsync(target, token).ConfigureAwait(false);
            EventRecord? template = r.TemplateEventId == null ? null : await ReadTemplateAsync(r.TemplateEventId, token).ConfigureAwait(false);
            if (template != null && template.PublishedUtc == null)
            {
                template.PublishedUtc = now;
                await _Services.Database.Events.Records.UpdateAsync(template, false, now, token).ConfigureAwait(false);
            }
            List<EventRecord> published = new List<EventRecord>();
            foreach (EventRecord o in await _Services.Database.Events.Records.ListOccurrencesAsync(r.Id, 5000, token).ConfigureAwait(false))
            {
                if (o.Status != EventStatusEnum.Draft || (o.EndUtc <= now && !String.Equals(o.Id, target.Id, StringComparison.Ordinal))) continue;
                o.Status = EventStatusEnum.Published;
                o.PublishedUtc = now;
                await _Services.Database.Events.Records.UpdateAsync(o, false, now, token).ConfigureAwait(false);
                await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Event, o.Id, token).ConfigureAwait(false);
                published.Add(o);
            }
            return published;
        }

        /// <summary>
        /// Delete a whole draft series (every occurrence, the template, and the recurrence).
        /// </summary>
        /// <param name="target">An occurrence.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        public async Task<int> DeleteSeriesAsync(EventRecord target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            DateTime now = _Clock();
            EventRecurrence r = await RequireRecurrenceAsync(target, token).ConfigureAwait(false);
            List<EventRecord> occurrences = await _Services.Database.Events.Records.ListOccurrencesAsync(r.Id, 5000, token).ConfigureAwait(false);
            if (occurrences.Any(o => o.Status != EventStatusEnum.Draft)) throw new ApiErrorException(409, "NotDraft", "Only draft series can be deleted; cancel published events instead.");
            List<string> ids = occurrences.Select(o => o.Id).ToList();
            if (r.TemplateEventId != null) ids.Add(r.TemplateEventId);
            int deleted = await _Services.Database.Events.Records.DeleteAsync(ids, now, token).ConfigureAwait(false);
            await _Services.Database.Events.Recurrences.DeleteAsync(r.Id, now, token).ConfigureAwait(false);
            return deleted;
        }

        /// <summary>
        /// The recurrence, template, occurrences, and exceptions of a series (series ICS).
        /// </summary>
        /// <param name="recurrenceId">Recurrence.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Recurrence or null.</returns>
        public Task<EventRecurrence?> ReadAsync(string recurrenceId, CancellationToken token = default)
        {
            return _Services.Database.Events.Recurrences.ReadAsync(recurrenceId, token);
        }

        /// <summary>
        /// The local date the rule produced for an occurrence (its original start, in the given zone).
        /// </summary>
        /// <param name="o">Occurrence.</param>
        /// <param name="zone">Zone.</param>
        /// <returns>Local date.</returns>
        public static DateTime OriginalDate(EventRecord o, TimeZoneInfo zone)
        {
            ArgumentNullException.ThrowIfNull(o);
            return EventTimeZones.ToLocal(o.OriginalStartUtc ?? o.StartUtc, zone).Date;
        }

        #endregion

        #region Private-Methods

        private static void SetRule(EventRecurrence r, EventRecurrenceRule rule)
        {
            r.Frequency = rule.Frequency;
            r.Interval = rule.Interval;
            r.Weekdays = new List<DayOfWeek>(rule.Weekdays);
            r.MonthDay = rule.MonthDay;
            r.SetPosition = rule.SetPosition;
            r.Count = rule.Count;
            r.UntilLocalDate = rule.UntilDate?.ToString(EventTimeZones.DateFormat, CultureInfo.InvariantCulture);
        }

        private async Task<EventRecurrence> RequireRecurrenceAsync(EventRecord target, CancellationToken token)
        {
            if (target.RecurrenceId == null) throw ApiErrorException.NotFound("Recurrence");
            EventRecurrence? r = await _Services.Database.Events.Recurrences.ReadAsync(target.RecurrenceId, token).ConfigureAwait(false);
            if (r == null || r.TemplateEventId == null) throw ApiErrorException.NotFound("Recurrence");
            return r;
        }

        private async Task<EventRecord?> ReadTemplateAsync(string templateId, CancellationToken token)
        {
            EventRecord? template = await _Services.Database.Events.Records.ReadAsync(templateId, token).ConfigureAwait(false);
            return template != null && template.Status == EventStatusEnum.Template ? template : null;
        }

        private static EventRecord FromTemplate(EventRecord template, EventRecurrence r, EventOccurrenceSlot slot, string? id, DateTime now)
        {
            EventRecord o = template.Clone();
            o.Id = id ?? IdGenerator.Generate(EventIdPrefixes.Event);
            o.Status = template.PublishedUtc != null ? EventStatusEnum.Published : EventStatusEnum.Draft;
            o.PublishedUtc = template.PublishedUtc != null ? now : null;
            o.StartUtc = slot.StartUtc;
            o.EndUtc = slot.EndUtc;
            o.TimeZone = r.TimeZone;
            o.IsAllDay = r.IsAllDay;
            o.RecurrenceId = r.Id;
            o.OriginalStartUtc = slot.StartUtc;
            o.OccurrenceIndex = slot.Index;
            o.IsException = false;
            o.CanceledUtc = null;
            o.CancelReason = null;
            o.CanceledByUserId = null;
            o.RsvpOpensUtc = null;
            o.RsvpClosesUtc = null;
            o.IcsSequence = 0;
            o.Slug = EventEditor.Slugify(template.Title) + "-" + slot.StartLocal.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            o.CreatedUtc = now;
            o.UpdatedUtc = now;
            return o;
        }

        private static EventRecord CopyFields(EventRecord source, EventRecord dest, HashSet<string> present)
        {
            EventRecord o = dest.Clone();
            foreach (string field in present)
            {
                if (_NotCopied.Contains(field)) continue;
                switch (field.ToLowerInvariant())
                {
                    case "title": o.Title = source.Title; break;
                    case "summary": o.Summary = source.Summary; break;
                    case "description": o.Description = source.Description; break;
                    case "category": o.CategoryKey = source.CategoryKey; break;
                    case "format":
                    case "location":
                    case "online":
                        o.Format = source.Format;
                        o.VenueId = source.VenueId;
                        o.LocationName = source.LocationName;
                        o.Line1 = source.Line1;
                        o.Line2 = source.Line2;
                        o.Locality = source.Locality;
                        o.Region = source.Region;
                        o.PostalCode = source.PostalCode;
                        o.CountryCode = source.CountryCode;
                        o.AddressVisibility = source.AddressVisibility;
                        o.Lat = source.Lat;
                        o.Lng = source.Lng;
                        o.OnlinePlatformLabel = source.OnlinePlatformLabel;
                        o.OnlineJoinUrl = source.OnlineJoinUrl;
                        break;
                    case "capacity": o.Capacity = source.Capacity; break;
                    case "maxguestsperrsvp": o.MaxGuestsPerRsvp = source.MaxGuestsPerRsvp; break;
                    case "waitlistenabled": o.WaitlistEnabled = source.WaitlistEnabled; break;
                    case "rsvpaudience": o.RsvpAudience = source.RsvpAudience; break;
                    case "attendeevisibility": o.AttendeeVisibility = source.AttendeeVisibility; break;
                    case "externalurl": o.ExternalUrl = source.ExternalUrl; break;
                    case "covermediaid": o.CoverMediaId = source.CoverMediaId; break;
                    case "hostuserids": o.HostUserIds = new List<string>(source.HostUserIds); break;
                    case "instructorids": o.InstructorIds = new List<string>(source.InstructorIds); break;
                    case "reminderoffsetsminutes": o.ReminderOffsetsMinutes = new List<int>(source.ReminderOffsetsMinutes); break;
                    case "commentsenabled": o.CommentsEnabled = source.CommentsEnabled; break;
                    case "audience": o.Audience = source.Audience; break;
                }
            }
            return o;
        }

        #endregion
    }
}
