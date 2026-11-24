namespace TrellisKit.Server.Services.Usage
{
    using System;

    /// <summary>
    /// A property's usage calendar: its time zone and first day of the week (docs/USAGE_METERING_CONTRACT.md section 3.1).
    /// </summary>
    public sealed class UsageZone
    {
        #region Public-Members

        /// <summary>
        /// UTC with Monday weeks.
        /// </summary>
        public static readonly UsageZone Utc = new UsageZone(TimeZoneInfo.Utc, "UTC", DayOfWeek.Monday);

        /// <summary>
        /// Time zone.
        /// </summary>
        public TimeZoneInfo Zone { get; }

        /// <summary>
        /// IANA id as configured.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// First day of the week.
        /// </summary>
        public DayOfWeek WeekStart { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a calendar.
        /// </summary>
        /// <param name="zone">Time zone.</param>
        /// <param name="id">Configured id.</param>
        /// <param name="weekStart">First day of the week.</param>
        /// <exception cref="ArgumentNullException">The zone or id is null.</exception>
        public UsageZone(TimeZoneInfo zone, string id, DayOfWeek weekStart)
        {
            Zone = zone ?? throw new ArgumentNullException(nameof(zone));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            Id = id;
            WeekStart = weekStart;
        }

        /// <summary>
        /// Build a calendar from setting values; an unknown zone id or day name falls back to UTC and Monday.
        /// </summary>
        /// <param name="zoneId">IANA zone id, or null.</param>
        /// <param name="weekStart">Day name, or null.</param>
        /// <returns>Calendar.</returns>
        public static UsageZone From(string? zoneId, string? weekStart)
        {
            TimeZoneInfo? zone = TryZone(zoneId);
            DayOfWeek day = Enum.TryParse(weekStart, true, out DayOfWeek parsed) && Enum.IsDefined(parsed) ? parsed : DayOfWeek.Monday;
            if (zone == null) return new UsageZone(TimeZoneInfo.Utc, "UTC", day);
            return new UsageZone(zone, zoneId!.Trim(), day);
        }

        /// <summary>
        /// Resolve an IANA zone id.
        /// </summary>
        /// <param name="zoneId">Zone id.</param>
        /// <returns>Zone, or null when unknown.</returns>
        public static TimeZoneInfo? TryZone(string? zoneId)
        {
            if (String.IsNullOrWhiteSpace(zoneId)) return null;
            string id = zoneId.Trim();
            if (String.Equals(id, "UTC", StringComparison.OrdinalIgnoreCase)) return TimeZoneInfo.Utc;
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                return null;
            }
            catch (InvalidTimeZoneException)
            {
                return null;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The local date containing an instant.
        /// </summary>
        /// <param name="utc">Instant (UTC).</param>
        /// <returns>Local date.</returns>
        public DateOnly LocalDate(DateTime utc)
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), Zone));
        }

        /// <summary>
        /// The local wall-clock time of an instant.
        /// </summary>
        /// <param name="utc">Instant (UTC).</param>
        /// <returns>Local time (unspecified kind).</returns>
        public DateTime Local(DateTime utc)
        {
            return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), Zone), DateTimeKind.Unspecified);
        }

        /// <summary>
        /// The instant of a local wall-clock time; a time skipped by a daylight saving change moves forward to the first
        /// valid time, and an ambiguous time resolves to its first occurrence.
        /// </summary>
        /// <param name="local">Local time.</param>
        /// <returns>Instant (UTC).</returns>
        public DateTime ToUtc(DateTime local)
        {
            DateTime t = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            for (int i = 0; i < 16 && Zone.IsInvalidTime(t); i++) t = t.AddMinutes(15);
            if (Zone.IsAmbiguousTime(t))
            {
                TimeSpan[] offsets = Zone.GetAmbiguousTimeOffsets(t);
                TimeSpan first = offsets[0] > offsets[offsets.Length - 1] ? offsets[0] : offsets[offsets.Length - 1];
                return DateTime.SpecifyKind(t - first, DateTimeKind.Utc);
            }
            return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(t, Zone), DateTimeKind.Utc);
        }

        /// <summary>
        /// Local midnight starting a date, as an instant.
        /// </summary>
        /// <param name="date">Local date.</param>
        /// <returns>Instant (UTC).</returns>
        public DateTime Midnight(DateOnly date)
        {
            return ToUtc(date.ToDateTime(TimeOnly.MinValue));
        }

        /// <summary>
        /// The first day of the week containing a date.
        /// </summary>
        /// <param name="date">Local date.</param>
        /// <returns>Week start date.</returns>
        public DateOnly WeekStartOf(DateOnly date)
        {
            int back = ((int)date.DayOfWeek - (int)WeekStart + 7) % 7;
            return date.AddDays(-back);
        }

        /// <summary>
        /// Shift an instant by whole local days, keeping the local wall-clock time.
        /// </summary>
        /// <param name="utc">Instant.</param>
        /// <param name="days">Days (negative moves back).</param>
        /// <returns>Instant.</returns>
        public DateTime ShiftDays(DateTime utc, int days)
        {
            return ToUtc(Local(utc).AddDays(days));
        }

        #endregion

        #region Private-Methods

        private static DateTime AsUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        #endregion
    }
}
