namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Calendars (docs/PHASE4_CONTRACT.md section 4.5; series from resolved decision 11): the single event ICS
    /// (SEQUENCE, VTIMEZONE, VALARM, STATUS:CANCELLED, all-day dates, RECURRENCE-ID for occurrences), the series ICS
    /// (master VEVENT with RRULE, EXDATE for canceled or deleted occurrences, and override VEVENTs for edited ones), and
    /// the personal subscription feed (token cfd_id.secret with the secret stored hashed; rotation and deletion
    /// invalidate it; a ban, account deletion, or lost access disables it). Thread safety: stateless.
    /// </summary>
    public class CalendarService
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
        /// Days of past events a feed keeps. Default 30.
        /// </summary>
        public const int FeedPastDays = 30;

        #endregion

        #region Private-Members

        private static readonly string[] _Days = new[] { "SU", "MO", "TU", "WE", "TH", "FR", "SA" };
        private readonly ApplicationServices _Services;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public CalendarService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// GET /api/v1/events/{id}/ics (scope Series for the whole series).
        /// </summary>
        /// <param name="actor">Caller (signed in).</param>
        /// <param name="id">Event.</param>
        /// <param name="series">Whether to export the series of an occurrence.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Document.</returns>
        /// <exception cref="ApiErrorException">404, 429.</exception>
        public async Task<IcsDocument> EventAsync(ContentActor actor, string id, bool series, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            EventRecord e = await _Services.Database.Events.Records.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Event");
            if (EventAccess.Read(e, actor) != EventReadEnum.Open) throw ApiErrorException.NotFound("Event");
            _Services.Limits.EnforceMember(actor, "event-ics", _Services.Settings.Events.IcsPerHour, RateLimitService.Hour);
            if (series && e.RecurrenceId != null)
            {
                IcsDocument doc = await SeriesAsync(actor, e, token).ConfigureAwait(false);
                EventTelemetry.Count(EventTelemetry.Calendar, 1, "kind", "series", "outcome", "success");
                return doc;
            }
            EventRsvp? mine = String.IsNullOrEmpty(actor.UserId) ? null : await _Services.Database.Events.Rsvps.ReadAsync(e.Id, actor.UserId, token).ConfigureAwait(false);
            DateTime now = _Clock();
            bool join = (mine != null && mine.Status == EventRsvpStatusEnum.Going || EventAccess.IsStaffOrHost(e, actor)) && now >= EventAccess.JoinUrlAvailableUtc(e, _Services.Settings.Events.JoinUrlRevealMinutes);
            EventRecurrence? r = e.RecurrenceId == null ? null : await _Services.Database.Events.Recurrences.ReadAsync(e.RecurrenceId, token).ConfigureAwait(false);
            TimeZoneInfo zone = EventTimeZones.Find(e.TimeZone) ?? TimeZoneInfo.Utc;
            IcsWriter w = Begin();
            w.TimeZone(zone, Min(e.StartUtc, e.OriginalStartUtc), e.EndUtc);
            await WriteEventAsync(w, e, actor, r == null ? Uid(e.Id) : Uid(r.Id), r == null ? null : e.OriginalStartUtc ?? e.StartUtc, join, true, token).ConfigureAwait(false);
            w.End("VCALENDAR");
            EventTelemetry.Count(EventTelemetry.Calendar, 1, "kind", "ics", "outcome", "success");
            return new IcsDocument { FileName = e.Slug + ".ics", Text = w.ToString() };
        }

        /// <summary>
        /// GET /api/v1/me/calendar-feed: the caller's feed (created on first call).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Feed with its URL.</returns>
        /// <exception cref="ApiErrorException">404 NotAvailable when feeds are off.</exception>
        public async Task<CalendarFeedView> GetFeedAsync(ContentActor actor, CancellationToken token = default)
        {
            string userId = actor.RequireUserId();
            RequireFeeds();
            CalendarFeed? feed = await _Services.Database.Events.Feeds.ReadForUserAsync(userId, token).ConfigureAwait(false);
            string? secret = feed == null ? null : Secret(feed.Id);
            if (feed == null || !feed.IsEnabled || secret == null || !String.Equals(Hash(secret), feed.SecretHash, StringComparison.Ordinal)) return await IssueAsync(userId, feed != null, token).ConfigureAwait(false);
            return new CalendarFeedView { Url = FeedUrl(feed.Id, secret), CreatedUtc = feed.CreatedUtc, RotatedUtc = feed.RotatedUtc, LastFetchedUtc = feed.LastFetchedUtc };
        }

        /// <summary>
        /// POST /api/v1/me/calendar-feed/rotate: a new URL; the old one stops working.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Feed with the new URL.</returns>
        /// <exception cref="ApiErrorException">404 NotAvailable when feeds are off.</exception>
        public Task<CalendarFeedView> RotateAsync(ContentActor actor, CancellationToken token = default)
        {
            string userId = actor.RequireUserId();
            RequireFeeds();
            return IssueAsync(userId, true, token);
        }

        /// <summary>
        /// DELETE /api/v1/me/calendar-feed.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteFeedAsync(ContentActor actor, CancellationToken token = default)
        {
            string userId = actor.RequireUserId();
            await _Services.Database.Events.Feeds.DeleteForUserAsync(userId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// GET /api/v1/calendar/{token}.ics: the member's Going and Waitlisted events from 30 days ago to
        /// events.calendarFeedDays ahead (at most events.calendarFeedMaxEvents), one VEVENT per occurrence, without join
        /// URLs or attendee data.
        /// </summary>
        /// <param name="feedToken">Token (cfd_id.secret, with or without .ics).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Document.</returns>
        /// <exception cref="ApiErrorException">404 for an unknown, rotated, or disabled token; 429.</exception>
        public async Task<IcsDocument> FeedAsync(string? feedToken, CancellationToken token = default)
        {
            if (!_Services.Settings.Events.CalendarFeedEnabled) throw ApiErrorException.NotFound("Calendar feed");
            string raw = (feedToken ?? String.Empty).Trim();
            if (raw.EndsWith(".ics", StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(0, raw.Length - 4);
            int dot = raw.IndexOf('.');
            if (dot <= 0 || dot == raw.Length - 1 || !raw.StartsWith(EventIdPrefixes.CalendarFeed, StringComparison.Ordinal)) throw Missing();
            string id = raw.Substring(0, dot);
            string secret = raw.Substring(dot + 1);
            CalendarFeed? feed = await _Services.Database.Events.Feeds.ReadAsync(id, token).ConfigureAwait(false);
            if (feed == null || !feed.IsEnabled || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(secret)), Encoding.ASCII.GetBytes(feed.SecretHash)))
            {
                EventTelemetry.Count(EventTelemetry.Calendar, 1, "kind", "feed", "outcome", "not_found");
                throw Missing();
            }
            _Services.Limits.Enforce("event-feed:" + feed.Id, _Services.Settings.Events.IcsPerHour, RateLimitService.Hour);
            ContentActor viewer;
            try
            {
                viewer = await _Services.Actors.ForUserAsync(feed.UserId, token).ConfigureAwait(false);
            }
            catch (ApiErrorException)
            {
                await _Services.Database.Events.Feeds.DisableForUserAsync(feed.UserId, token).ConfigureAwait(false);
                throw Missing();
            }
            if (viewer.User == null || viewer.User.IsBanned || (viewer.Access.Model == AccessModelEnum.FullyGated && !viewer.HasMembership && !viewer.IsSystemAdmin))
            {
                await _Services.Database.Events.Feeds.DisableForUserAsync(feed.UserId, token).ConfigureAwait(false);
                EventTelemetry.Count(EventTelemetry.Calendar, 1, "kind", "feed", "outcome", "disabled");
                throw Missing();
            }
            DateTime now = _Clock();
            List<EventRecord> events = await _Services.Database.Events.Records.ListFeedEventsAsync(feed.UserId, now.AddDays(-FeedPastDays), now.AddDays(_Services.Settings.Events.CalendarFeedDays),
                _Services.Settings.Events.CalendarFeedMaxEvents, token).ConfigureAwait(false);
            events = events.Where(e => EventAccess.Read(e, viewer) == EventReadEnum.Open).ToList();
            IcsWriter w = Begin();
            w.Text("X-WR-CALNAME", _Services.Property.Name + " events");
            foreach (string zoneId in events.Select(e => e.TimeZone).Distinct(StringComparer.Ordinal))
            {
                TimeZoneInfo? zone = EventTimeZones.Find(zoneId);
                if (zone != null) w.TimeZone(zone, now.AddDays(-FeedPastDays), now.AddDays(_Services.Settings.Events.CalendarFeedDays + 2));
            }
            foreach (EventRecord e in events) await WriteEventAsync(w, e, viewer, Uid(e.Id), null, false, false, token).ConfigureAwait(false);
            w.End("VCALENDAR");
            await _Services.Database.Events.Feeds.TouchAsync(feed.Id, now, token).ConfigureAwait(false);
            EventTelemetry.Count(EventTelemetry.Calendar, 1, "kind", "feed", "outcome", "success");
            return new IcsDocument { FileName = "events.ics", Text = w.ToString(), IsFeed = true };
        }

        /// <summary>
        /// The RRULE value of a recurrence, for example FREQ=WEEKLY;INTERVAL=1;BYDAY=TU,TH;COUNT=12.
        /// </summary>
        /// <param name="r">Recurrence.</param>
        /// <param name="zone">Zone.</param>
        /// <param name="anchor">Local start of the series.</param>
        /// <param name="maxOccurrences">events.maxOccurrences (open-ended rules stop there).</param>
        /// <returns>RRULE value.</returns>
        public static string Rrule(EventRecurrence r, TimeZoneInfo zone, DateTime anchor, int maxOccurrences)
        {
            ArgumentNullException.ThrowIfNull(r);
            ArgumentNullException.ThrowIfNull(zone);
            List<string> parts = new List<string> { "FREQ=" + r.Frequency.ToString().ToUpperInvariant(), "INTERVAL=" + Math.Max(1, r.Interval).ToString(CultureInfo.InvariantCulture) };
            if (r.Frequency == EventRecurrenceFrequencyEnum.Weekly && r.Weekdays.Count > 0) parts.Add("BYDAY=" + String.Join(",", r.Weekdays.OrderBy(d => ((int)d + 6) % 7).Select(d => _Days[(int)d])));
            if (r.Frequency == EventRecurrenceFrequencyEnum.Monthly)
            {
                if (r.SetPosition != null && r.Weekdays.Count > 0) parts.Add("BYDAY=" + r.SetPosition.Value.ToString(CultureInfo.InvariantCulture) + _Days[(int)r.Weekdays[0]]);
                else if (r.MonthDay != null) parts.Add("BYMONTHDAY=" + r.MonthDay.Value.ToString(CultureInfo.InvariantCulture));
            }
            if (r.Count != null) parts.Add("COUNT=" + r.Count.Value.ToString(CultureInfo.InvariantCulture));
            else if (!String.IsNullOrEmpty(r.UntilLocalDate) && DateTime.TryParseExact(r.UntilLocalDate, EventTimeZones.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime until))
            {
                DateTime last = r.IsAllDay ? until.Date : until.Date + anchor.TimeOfDay;
                parts.Add("UNTIL=" + (r.IsAllDay ? IcsWriter.DateStamp(last) : IcsWriter.UtcStamp(EventTimeZones.ToUtcLenient(last, zone))));
            }
            else parts.Add("COUNT=" + Math.Max(1, maxOccurrences).ToString(CultureInfo.InvariantCulture));
            return String.Join(";", parts);
        }

        #endregion

        #region Private-Methods

        private async Task<IcsDocument> SeriesAsync(ContentActor actor, EventRecord e, CancellationToken token)
        {
            EventRecurrence r = await _Services.Database.Events.Recurrences.ReadAsync(e.RecurrenceId!, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Event");
            EventRecord template = (r.TemplateEventId == null ? null : await _Services.Database.Events.Records.ReadAsync(r.TemplateEventId, token).ConfigureAwait(false)) ?? e;
            TimeZoneInfo zone = EventTimeZones.Find(r.TimeZone) ?? TimeZoneInfo.Utc;
            if (!EventTimeZones.TryParseLocal(r.StartLocal, out DateTime anchor)) anchor = EventTimeZones.ToLocal(template.StartUtc, zone);
            List<EventRecord> occurrences = await _Services.Database.Events.Records.ListOccurrencesAsync(r.Id, 5000, token).ConfigureAwait(false);
            List<EventRecurrenceException> exceptions = await _Services.Database.Events.Recurrences.ListExceptionsAsync(r.Id, token).ConfigureAwait(false);
            EventRecurrenceRule rule = RecurrenceService.RuleOf(r);
            DateTime masterStart = EventTimeZones.ToUtcLenient(anchor, zone);
            DateTime last = occurrences.Count == 0 ? masterStart.AddDays(1) : occurrences.Max(o => o.EndUtc);
            IcsWriter w = Begin();
            w.TimeZone(zone, masterStart, last.AddDays(_Services.Settings.Events.RecurrenceHorizonDays));
            string uid = Uid(r.Id);
            EventRecord master = template.Clone();
            master.StartUtc = masterStart;
            master.EndUtc = EventTimeZones.ToUtcLenient(anchor.AddMinutes(r.DurationMinutes), zone);
            master.Status = EventStatusEnum.Published;
            master.IcsSequence = r.IcsSequence;
            master.TimeZone = zone.Id;
            master.IsAllDay = r.IsAllDay;
            if (master.IsAllDay) master.EndUtc = EventTimeZones.ToUtcLenient(anchor.Date.AddDays(Math.Max(1, (int)Math.Round(r.DurationMinutes / 1440.0))), zone);
            List<string> extra = new List<string> { "RRULE:" + Rrule(r, zone, anchor, _Services.Settings.Events.MaxOccurrences) };
            HashSet<DateTime> excluded = new HashSet<DateTime>();
            foreach (EventRecurrenceException x in exceptions.Where(x => x.Kind != EventRecurrenceExceptionKindEnum.Edited)) excluded.Add(x.OriginalStartUtc);
            foreach (EventRecord o in occurrences.Where(o => o.Status == EventStatusEnum.Canceled)) excluded.Add(o.OriginalStartUtc ?? o.StartUtc);
            foreach (DateTime original in excluded.OrderBy(d => d))
            {
                DateTime local = EventTimeZones.ToLocal(original, zone);
                if (!RecurrenceExpander.Matches(rule, anchor.Date, local.Date)) continue;
                extra.Add(r.IsAllDay ? "EXDATE;VALUE=DATE:" + IcsWriter.DateStamp(local.Date) : "EXDATE;TZID=" + zone.Id + ":" + IcsWriter.LocalStamp(local));
            }
            await WriteEventAsync(w, master, actor, uid, null, false, true, token, extra, e.Id).ConfigureAwait(false);
            foreach (EventRecord o in occurrences.Where(o => o.IsException && o.Status != EventStatusEnum.Canceled))
                await WriteEventAsync(w, o, actor, uid, o.OriginalStartUtc ?? o.StartUtc, false, true, token).ConfigureAwait(false);
            w.End("VCALENDAR");
            return new IcsDocument { FileName = e.Slug + "-series.ics", Text = w.ToString() };
        }

        private async Task WriteEventAsync(IcsWriter w, EventRecord e, ContentActor viewer, string uid, DateTime? recurrenceIdUtc, bool includeJoin, bool alarms, CancellationToken token,
            List<string>? extraLines = null, string? urlEventId = null)
        {
            TimeZoneInfo zone = EventTimeZones.Find(e.TimeZone) ?? TimeZoneInfo.Utc;
            DateTime now = _Clock();
            w.Begin("VEVENT");
            w.Raw("UID", uid);
            w.Raw("DTSTAMP", IcsWriter.UtcStamp(now));
            w.Raw("SEQUENCE", e.IcsSequence.ToString(CultureInfo.InvariantCulture));
            if (e.IsAllDay)
            {
                w.Raw("DTSTART;VALUE=DATE", IcsWriter.DateStamp(EventTimeZones.ToLocal(e.StartUtc, zone).Date));
                w.Raw("DTEND;VALUE=DATE", IcsWriter.DateStamp(EventTimeZones.ToLocal(e.EndUtc, zone).Date));
            }
            else
            {
                w.Raw("DTSTART;TZID=" + zone.Id, IcsWriter.LocalStamp(EventTimeZones.ToLocal(e.StartUtc, zone)));
                w.Raw("DTEND;TZID=" + zone.Id, IcsWriter.LocalStamp(EventTimeZones.ToLocal(e.EndUtc, zone)));
            }
            if (recurrenceIdUtc != null)
            {
                DateTime original = EventTimeZones.ToLocal(recurrenceIdUtc.Value, zone);
                if (e.IsAllDay) w.Raw("RECURRENCE-ID;VALUE=DATE", IcsWriter.DateStamp(original.Date));
                else w.Raw("RECURRENCE-ID;TZID=" + zone.Id, IcsWriter.LocalStamp(original));
            }
            foreach (string line in extraLines ?? new List<string>())
            {
                int colon = line.IndexOf(':');
                w.Raw(line.Substring(0, colon), line.Substring(colon + 1));
            }
            w.Text("SUMMARY", e.Title);
            string link = _Services.Events.Notifier.EventLink(urlEventId ?? e.Id);
            StringBuilder description = new StringBuilder();
            string plain = MarkdownNormalizer.ToPlainText(e.Description);
            if (plain.Length > 1000) plain = plain.Substring(0, 1000) + "...";
            if (plain.Length > 0) description.Append(plain).Append("\n\n");
            if (!String.IsNullOrWhiteSpace(e.Summary) && plain.Length == 0) description.Append(e.Summary!.Trim()).Append("\n\n");
            if (e.Status == EventStatusEnum.Canceled && !String.IsNullOrWhiteSpace(e.CancelReason)) description.Append("Canceled: ").Append(e.CancelReason).Append("\n\n");
            if (includeJoin && !String.IsNullOrWhiteSpace(e.OnlineJoinUrl)) description.Append("Join: ").Append(e.OnlineJoinUrl).Append("\n\n");
            description.Append(link);
            w.Text("DESCRIPTION", description.ToString());
            EventVenueSnapshot? venue = e.VenueId == null ? null : await _Services.Database.Events.Records.ReadVenueAsync(e.VenueId, token).ConfigureAwait(false);
            string location = EventViewBuilder.LocationText(EventViewBuilder.Location(e, venue, viewer));
            if (location.Length == 0 && e.Format != EventFormatEnum.InPerson) location = String.IsNullOrWhiteSpace(e.OnlinePlatformLabel) ? "Online" : e.OnlinePlatformLabel!;
            if (location.Length > 0) w.Text("LOCATION", location);
            w.Raw("URL", link);
            w.Raw("STATUS", e.Status == EventStatusEnum.Canceled ? "CANCELLED" : "CONFIRMED");
            w.Raw("CREATED", IcsWriter.UtcStamp(e.CreatedUtc));
            w.Raw("LAST-MODIFIED", IcsWriter.UtcStamp(e.UpdatedUtc));
            if (alarms && e.Status != EventStatusEnum.Canceled)
            {
                foreach (int offset in e.ReminderOffsetsMinutes.OrderByDescending(o => o))
                {
                    w.Begin("VALARM");
                    w.Raw("ACTION", "DISPLAY");
                    w.Text("DESCRIPTION", e.Title);
                    w.Raw("TRIGGER", "-PT" + offset.ToString(CultureInfo.InvariantCulture) + "M");
                    w.End("VALARM");
                }
            }
            w.End("VEVENT");
        }

        private IcsWriter Begin()
        {
            IcsWriter w = new IcsWriter();
            w.Begin("VCALENDAR");
            w.Raw("VERSION", "2.0");
            w.Text("PRODID", "-//TrellisKit//" + _Services.Property.Name + "//EN");
            w.Raw("CALSCALE", "GREGORIAN");
            w.Raw("METHOD", "PUBLISH");
            return w;
        }

        private string Uid(string id)
        {
            string api = _Services.Property.Property.Domains.Api;
            string domain = String.IsNullOrWhiteSpace(api) ? (new Uri(_Services.Property.PublicBaseUrl).Host) : api.Trim();
            return id + "@" + domain;
        }

        private async Task<CalendarFeedView> IssueAsync(string userId, bool rotate, CancellationToken token)
        {
            DateTime now = _Clock();
            string id = IdGenerator.Generate(EventIdPrefixes.CalendarFeed);
            string secret = Secret(id) ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            CalendarFeed feed = await _Services.Database.Events.Feeds.UpsertAsync(new CalendarFeed
            {
                Id = id,
                UserId = userId,
                SecretHash = Hash(secret),
                IsEnabled = true,
                CreatedUtc = now,
                RotatedUtc = rotate ? now : null
            }, token).ConfigureAwait(false);
            return new CalendarFeedView { Url = FeedUrl(feed.Id, secret), CreatedUtc = feed.CreatedUtc, RotatedUtc = feed.RotatedUtc, LastFetchedUtc = feed.LastFetchedUtc };
        }

        private string FeedUrl(string id, string secret)
        {
            return _Services.Events.Notifier.ApiOrigin() + Constants.ApiPrefix + "/calendar/" + id + "." + secret + ".ics";
        }

        private string? Secret(string feedId)
        {
            string configured = (_Services.Settings.Auth.TokenEncryptionKey ?? String.Empty).Trim();
            if (configured.Length == 0) return null;
            byte[] material;
            try
            {
                material = Convert.FromBase64String(configured);
            }
            catch (FormatException)
            {
                material = Encoding.UTF8.GetBytes(configured);
            }
            byte[] key = HKDF.DeriveKey(HashAlgorithmName.SHA256, material, 32, Array.Empty<byte>(), Encoding.UTF8.GetBytes("tk-calendar-feed"));
            byte[] mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(_Services.PropertyId + "|" + feedId));
            return Convert.ToBase64String(mac, 0, 24).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private void RequireFeeds()
        {
            if (!_Services.Settings.Events.CalendarFeedEnabled) throw new ApiErrorException(404, "NotAvailable", "Calendar feeds are turned off for this property.");
        }

        private static string Hash(string secret)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();
        }

        private static DateTime Min(DateTime a, DateTime? b)
        {
            return b != null && b.Value < a ? b.Value : a;
        }

        private static ApiErrorException Missing()
        {
            return ApiErrorException.NotFound("Calendar feed");
        }

        #endregion
    }
}
