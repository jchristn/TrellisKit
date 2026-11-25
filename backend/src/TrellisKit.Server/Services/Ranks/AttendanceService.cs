namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Attendance (docs/TRELLISKIT.md, Ranks, curricula, and attendance): members check in to an event or to a venue
    /// schedule session on one date with the target's rotating code inside the check-in window (measured from the event
    /// start, or the session start on the date in the venue's time zone); rank awarders check members in. Thread safety:
    /// safe for concurrent use.
    /// </summary>
    public class AttendanceService
    {
        #region Private-Members

        private readonly ApplicationServices _Services;
        private readonly RankModule _Module;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <param name="module">Rank module.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public AttendanceService(ApplicationServices services, RankModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/attendance/check-in. A member checks in with the target's current code inside the window (method
        /// QR or SelfWithCode); a rank awarder checks another member in with userId (method Staff, no code; any time
        /// from the window's start). Audited attendance.checkIn.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The check-in.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidCode; 403 Forbidden; 404 NotAvailable or NotFound; 409 AlreadyCheckedIn; 422 OutsideCheckInWindow.</exception>
        public async Task<AttendanceView> CheckInAsync(ContentActor actor, RequestInfo info, AttendanceCheckInRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            _Module.RequireEnabled();
            string? self = actor.UserId;
            string? userId = String.IsNullOrWhiteSpace(request.UserId) ? self : request.UserId.Trim();
            if (userId == null) throw ApiErrorException.Field("userId", "Required for the admin token.");
            bool staff = userId != self;
            if (staff && !actor.IsRankStaff) throw ApiErrorException.Forbidden("Only rank awarders (RankAwarder or SystemAdmin) can check other members in.");
            AttendanceMethodEnum method = AttendanceMethodEnum.Staff;
            if (!staff)
            {
                method = AttendanceMethodEnum.SelfWithCode;
                if (!String.IsNullOrWhiteSpace(request.Method))
                {
                    if (String.Equals(request.Method.Trim(), "QR", StringComparison.OrdinalIgnoreCase)) method = AttendanceMethodEnum.QR;
                    else if (!String.Equals(request.Method.Trim(), "SelfWithCode", StringComparison.OrdinalIgnoreCase)) throw ApiErrorException.Field("method", "Use QR or SelfWithCode.");
                }
                if (String.IsNullOrWhiteSpace(request.Code)) throw ApiErrorException.Field("code", "Required.");
            }
            CheckInTarget target = await ResolveAsync(request.TargetType, request.TargetId, request.OccurrenceDate, token).ConfigureAwait(false);
            User? user = await _Services.Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted || user.IsBanned) throw ApiErrorException.NotFound("Member");
            DateTime now = DateTime.UtcNow;
            DateTime opens = target.StartUtc.AddMinutes(-_Services.Settings.Attendance.CheckInWindowBeforeMinutes);
            DateTime closes = target.StartUtc.AddMinutes(_Services.Settings.Attendance.CheckInWindowAfterMinutes);
            if (now < opens || (!staff && now > closes))
                throw new ApiErrorException(422, "OutsideCheckInWindow", "Check-in is open from " + opens.ToString("u", CultureInfo.InvariantCulture) + " to " + closes.ToString("u", CultureInfo.InvariantCulture) + ".");
            if (!staff && !CheckInCodes.Matches(Secret(), _Services.PropertyId, target, request.Code, now, _Services.Settings.Attendance.CodeRotationSeconds))
                throw new ApiErrorException(400, "InvalidCode", "That code is not valid. Check the code shown by the instructor and try again.");
            ObjectMetadata metadata = await _Services.Metadata.PrepareNewAsync(actor, TargetTypeEnum.Attendance, request.Labels, request.Tags, token).ConfigureAwait(false);
            AttendanceView record = new AttendanceView
            {
                Id = IdGenerator.Generate(RankIds.Attendance),
                UserId = user.Id,
                TargetType = target.TargetType,
                TargetId = target.TargetId,
                OccurrenceDate = target.OccurrenceDate,
                CheckedInUtc = now,
                Method = method,
                CheckedInBy = staff ? actor.UserId : null,
                CreatedUtc = now
            };
            try
            {
                await _Services.Database.Attendance.CreateAsync(record, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "AlreadyCheckedIn", "This member is already checked in to this occurrence.");
            }
            if (metadata.Labels.Count > 0 || metadata.Tags.Count > 0) await _Services.Database.Metadata.WriteAsync(TargetTypeEnum.Attendance, record.Id, metadata, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "attendance.checkIn", "User", user.Id, method + " check-in to " + target.TargetType + " " + target.TargetId + " on " + target.OccurrenceDate, token).ConfigureAwait(false);
            record.Labels = metadata.Labels;
            record.Tags = metadata.Tags;
            return record;
        }

        /// <summary>
        /// GET /api/v1/attendance/check-in-code: the target's current rotating code for the staff screen.
        /// </summary>
        /// <param name="actor">Caller (RankAwarder or SystemAdmin).</param>
        /// <param name="targetType">Event or VenueSession.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="occurrenceDate">Occurrence date (required for VenueSession).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Code.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotAvailable or NotFound.</exception>
        public async Task<CheckInCodeResponse> CodeAsync(ContentActor actor, string? targetType, string? targetId, string? occurrenceDate, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            if (!actor.IsRankStaff) throw ApiErrorException.Forbidden("Only rank awarders (RankAwarder or SystemAdmin) can show check-in codes.");
            CheckInTarget target = await ResolveAsync(targetType, targetId, occurrenceDate, token).ConfigureAwait(false);
            int rotation = _Services.Settings.Attendance.CodeRotationSeconds;
            DateTime now = DateTime.UtcNow;
            long period = CheckInCodes.Period(now, rotation);
            string code = CheckInCodes.Code(Secret(), _Services.PropertyId, target, period);
            return new CheckInCodeResponse
            {
                TargetType = target.TargetType.ToString(),
                TargetId = target.TargetId,
                OccurrenceDate = target.OccurrenceDate,
                Code = code,
                RotationSeconds = rotation,
                ExpiresUtc = DateTimeOffset.FromUnixTimeSeconds((period + 1) * rotation).UtcDateTime,
                QrPayload = "checkin?targetType=" + Uri.EscapeDataString(target.TargetType.ToString()) + "&targetId=" + Uri.EscapeDataString(target.TargetId) +
                    "&occurrenceDate=" + Uri.EscapeDataString(target.OccurrenceDate) + "&code=" + code,
                WindowOpensUtc = target.StartUtc.AddMinutes(-_Services.Settings.Attendance.CheckInWindowBeforeMinutes),
                WindowClosesUtc = target.StartUtc.AddMinutes(_Services.Settings.Attendance.CheckInWindowAfterMinutes)
            };
        }

        /// <summary>
        /// GET /api/v1/admin/attendance (AttendanceCollections.Roster).
        /// </summary>
        /// <param name="actor">Caller (RankAwarder or SystemAdmin).</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden, 404 NotAvailable.</exception>
        public async Task<EnumerationResult<AttendanceView>> RosterAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            if (!actor.IsRankStaff) throw ApiErrorException.Forbidden("Only rank awarders (RankAwarder or SystemAdmin) can read attendance.");
            EnumerationQuery q = query ?? new EnumerationQuery();
            AttendanceFilter filter = Filter(q);
            filter.UserId = q.FilterString("userId");
            EnumerationRequest request = _Services.Enumeration.Prepare(q, AttendanceCollections.Roster, actor.IsStaffFor(TargetTypeEnum.Attendance), "attendance");
            EnumerationResult<AttendanceView> page = await _Services.Database.Attendance.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> summaries = await _Services.Summaries.BuildAsync(page.Objects.Select(a => a.UserId), token).ConfigureAwait(false);
            foreach (AttendanceView view in page.Objects) view.User = summaries.TryGetValue(view.UserId, out UserSummary? summary) ? summary : null;
            await _Services.Metadata.AttachAsync(TargetTypeEnum.Attendance, page.Objects, a => a.Id, actor.IsStaffFor(TargetTypeEnum.Attendance), token).ConfigureAwait(false);
            return page;
        }

        /// <summary>
        /// GET /api/v1/me/attendance (AttendanceCollections.Mine): the caller's own check-ins.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 for the admin token, 404 NotAvailable.</exception>
        public async Task<EnumerationResult<AttendanceView>> MineAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            string userId = actor.RequireUserId();
            EnumerationQuery q = query ?? new EnumerationQuery();
            AttendanceFilter filter = Filter(q);
            filter.UserId = userId;
            EnumerationRequest request = _Services.Enumeration.Prepare(q, AttendanceCollections.Mine, false, "my-attendance|" + userId);
            EnumerationResult<AttendanceView> page = await _Services.Database.Attendance.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            await _Services.Metadata.AttachAsync(TargetTypeEnum.Attendance, page.Objects, a => a.Id, false, token).ConfigureAwait(false);
            return page;
        }

        /// <summary>
        /// DELETE /api/v1/admin/attendance/{id}: remove a check-in recorded by mistake. Audited attendance.delete.
        /// </summary>
        /// <param name="actor">Caller (RankAwarder or SystemAdmin).</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Check-in.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotAvailable or NotFound.</exception>
        public async Task DeleteAsync(ContentActor actor, RequestInfo info, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            _Module.RequireEnabled();
            if (!actor.IsRankStaff) throw ApiErrorException.Forbidden("Only rank awarders (RankAwarder or SystemAdmin) can remove check-ins.");
            AttendanceView? record = await _Services.Database.Attendance.ReadAsync(id, token).ConfigureAwait(false);
            if (record == null) throw ApiErrorException.NotFound("Check-in");
            if (!String.IsNullOrEmpty(actor.UserId) && actor.UserId == record.UserId && !actor.IsSystemAdmin) throw ApiErrorException.Forbidden("You cannot remove your own check-in.");
            if (!await _Services.Database.Attendance.DeleteAsync(record.Id, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Check-in");
            await _Services.Audit.WriteAsync(info, "attendance.delete", "User", record.UserId, "Removed check-in to " + record.TargetType + " " + record.TargetId + " on " + record.OccurrenceDate, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolve a check-in target occurrence: a Published, uncanceled event (its occurrence date is its start date in
        /// its time zone), or a session of an active venue's structured schedule on a date whose weekday matches the
        /// session (its start is the session's start time on that date in the schedule's time zone).
        /// </summary>
        /// <param name="targetType">Event or VenueSession.</param>
        /// <param name="targetId">Event identifier, or {venueId}:{sessionIndex} (zero-based position in schedule.sessions).</param>
        /// <param name="occurrenceDate">yyyy-MM-dd (required for VenueSession; optional for Event and then must match).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Target.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task<CheckInTarget> ResolveAsync(string? targetType, string? targetId, string? occurrenceDate, CancellationToken token = default)
        {
            if (!Enum.TryParse(targetType, true, out AttendanceTargetTypeEnum type) || !Enum.IsDefined(type) || Int32.TryParse(targetType, out _))
                throw ApiErrorException.Field("targetType", "Use Event or VenueSession.");
            string id = (targetId ?? String.Empty).Trim();
            if (id.Length == 0) throw ApiErrorException.Field("targetId", "Required.");
            DateTime? date = null;
            if (!String.IsNullOrWhiteSpace(occurrenceDate))
            {
                if (!DateTime.TryParseExact(occurrenceDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)) throw ApiErrorException.Field("occurrenceDate", "Use yyyy-MM-dd.");
                date = parsed.Date;
            }
            if (type == AttendanceTargetTypeEnum.Event)
            {
                EventRecord? record = await _Services.Database.Events.Records.ReadAsync(id, token).ConfigureAwait(false);
                if (record == null || record.DeletedUtc != null || record.Status != EventStatusEnum.Published) throw ApiErrorException.NotFound("Event");
                TimeZoneInfo zone = EventTimeZones.Find(record.TimeZone) ?? TimeZoneInfo.Utc;
                string start = EventTimeZones.ToLocal(record.StartUtc, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (date != null && date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != start) throw ApiErrorException.Field("occurrenceDate", "Use the event's start date (" + start + ").");
                return new CheckInTarget { TargetType = type, TargetId = record.Id, OccurrenceDate = start, StartUtc = DateTime.SpecifyKind(record.StartUtc, DateTimeKind.Utc), TimeZone = zone.Id };
            }
            if (date == null) throw ApiErrorException.Field("occurrenceDate", "Required for a venue session.");
            int colon = id.LastIndexOf(':');
            if (colon <= 0 || !Int32.TryParse(id.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int index)) throw ApiErrorException.Field("targetId", "Use {venueId}:{sessionIndex}.");
            Venue? venue = await _Services.Database.DirectoryListings.Venues.ReadAsync(id.Substring(0, colon), token).ConfigureAwait(false);
            if (venue == null || venue.DeletedUtc != null || venue.Status != VenueStatusEnum.Active || venue.Schedule == null || index < 0 || index >= venue.Schedule.Sessions.Count)
                throw ApiErrorException.NotFound("Venue session");
            VenueScheduleEntry session = venue.Schedule.Sessions[index];
            TimeZoneInfo venueZone = EventTimeZones.Find(venue.Schedule.TimeZone ?? venue.TimeZone) ?? TimeZoneInfo.Utc;
            int weekday = ((int)date.Value.DayOfWeek + 6) % 7;
            if (weekday != session.DayOfWeek) throw ApiErrorException.Field("occurrenceDate", "This session meets on " + DayName(session.DayOfWeek) + "s.");
            if (!TimeSpan.TryParseExact(session.StartTime, "hh\\:mm", CultureInfo.InvariantCulture, out TimeSpan startTime)) throw ApiErrorException.NotFound("Venue session");
            DateTime local = DateTime.SpecifyKind(date.Value.Add(startTime), DateTimeKind.Unspecified);
            return new CheckInTarget
            {
                TargetType = type,
                TargetId = venue.Id + ":" + index.ToString(CultureInfo.InvariantCulture),
                OccurrenceDate = date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                StartUtc = EventTimeZones.ToUtcLenient(local, venueZone),
                TimeZone = venueZone.Id
            };
        }

        #endregion

        #region Private-Methods

        private string Secret()
        {
            string key = (_Services.Settings.Auth.TokenEncryptionKey ?? String.Empty).Trim();
            if (key.Length > 0) return key;
            return (_Services.Settings.Secrets.EncryptionKey ?? String.Empty).Trim();
        }

        private static AttendanceFilter Filter(EnumerationQuery q)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            AttendanceFilter filter = new AttendanceFilter
            {
                TargetType = q.FilterEnum<AttendanceTargetTypeEnum>("targetType", errors),
                TargetId = q.FilterString("targetId"),
                OccurrenceDate = q.FilterString("occurrenceDate")
            };
            if (filter.OccurrenceDate != null && !DateTime.TryParseExact(filter.OccurrenceDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                errors["filters.occurrenceDate"] = "Use yyyy-MM-dd.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            return filter;
        }

        private static string DayName(int day)
        {
            string[] names = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
            return day >= 0 && day < names.Length ? names[day] : "another day";
        }

        #endregion
    }
}
