namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Globalization;
    using TrellisKit.Core.Exceptions;

    /// <summary>
    /// Local wall time and IANA time zones for events (docs/PHASE4_CONTRACT.md section 4.6). Staff enter local time plus
    /// a zone; the engine converts with the IANA database (TimeZoneInfo with IANA ids), rejects nonexistent local times
    /// in a spring-forward gap (400 InvalidSchedule with a hint), and resolves ambiguous fall-back times to the first
    /// occurrence (the earlier instant). Recurrence expansion uses the lenient form, which moves a gap time forward by
    /// the gap (RFC 5545 section 3.3.5). Thread safety: stateless apart from a concurrent zone cache.
    /// </summary>
    public static class EventTimeZones
    {
        #region Public-Members

        /// <summary>
        /// Wire format of a local date and time.
        /// </summary>
        public const string LocalFormat = "yyyy-MM-ddTHH:mm";

        /// <summary>
        /// Wire format of a local date.
        /// </summary>
        public const string DateFormat = "yyyy-MM-dd";

        #endregion

        #region Private-Members

        private static readonly ConcurrentDictionary<string, TimeZoneInfo?> _Zones = new ConcurrentDictionary<string, TimeZoneInfo?>(StringComparer.Ordinal);
        private static readonly string[] _LocalFormats = new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss.fff" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// The zone of an IANA id (for example America/New_York), or null when unknown. UTC and Etc/UTC are accepted.
        /// </summary>
        /// <param name="id">IANA id.</param>
        /// <returns>Zone or null.</returns>
        public static TimeZoneInfo? Find(string? id)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            string key = id.Trim();
            return _Zones.GetOrAdd(key, k =>
            {
                if (k.Length > 64 || k.IndexOf('/') < 0 && k != "UTC" && k != "Etc/UTC") return null;
                try
                {
                    TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(k);
                    return zone;
                }
                catch (TimeZoneNotFoundException)
                {
                    return null;
                }
                catch (InvalidTimeZoneException)
                {
                    return null;
                }
            });
        }

        /// <summary>
        /// The zone of an IANA id or 400 InvalidSchedule.
        /// </summary>
        /// <param name="id">IANA id.</param>
        /// <param name="field">Field name for the error.</param>
        /// <returns>Zone.</returns>
        /// <exception cref="ApiErrorException">400 InvalidSchedule for an unknown zone.</exception>
        public static TimeZoneInfo Require(string? id, string field = "timeZone")
        {
            TimeZoneInfo? zone = Find(id);
            if (zone == null) throw Invalid(field, "Use an IANA time zone such as America/New_York.");
            return zone;
        }

        /// <summary>
        /// Parse a local date and time (yyyy-MM-ddTHH:mm, seconds optional) or a local date (yyyy-MM-dd, midnight).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="value">Local value (unspecified kind).</param>
        /// <returns>True when parsed.</returns>
        public static bool TryParseLocal(string? text, out DateTime value)
        {
            value = DateTime.MinValue;
            if (String.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            if (t.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || t.Length > 25) return false;
            if (DateTime.TryParseExact(t, _LocalFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
            {
                value = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
                return true;
            }
            if (DateTime.TryParseExact(t, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            {
                value = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Convert a local wall time to UTC strictly: a time inside a spring-forward gap is 400 InvalidSchedule with a
        /// hint; an ambiguous fall-back time resolves to the first occurrence (the daylight offset).
        /// </summary>
        /// <param name="local">Local wall time.</param>
        /// <param name="zone">Zone.</param>
        /// <param name="field">Field name for the error.</param>
        /// <returns>UTC instant (kind Utc).</returns>
        /// <exception cref="ArgumentNullException">The zone is null.</exception>
        /// <exception cref="ApiErrorException">400 InvalidSchedule for a nonexistent local time.</exception>
        public static DateTime ToUtcStrict(DateTime local, TimeZoneInfo zone, string field = "startLocal")
        {
            ArgumentNullException.ThrowIfNull(zone);
            DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(unspecified))
            {
                DateTime after = unspecified;
                while (zone.IsInvalidTime(after)) after = after.AddMinutes(15);
                throw Invalid(field, unspecified.ToString(LocalFormat, CultureInfo.InvariantCulture) + " does not exist in " + zone.Id + " (the clocks move forward then). Choose a time such as " +
                    after.ToString("HH:mm", CultureInfo.InvariantCulture) + ".");
            }
            return ToUtcLenient(unspecified, zone);
        }

        /// <summary>
        /// Convert a local wall time to UTC leniently: a gap time moves forward by the gap (it keeps the offset in effect
        /// before the transition), and an ambiguous time resolves to the first occurrence.
        /// </summary>
        /// <param name="local">Local wall time.</param>
        /// <param name="zone">Zone.</param>
        /// <returns>UTC instant (kind Utc).</returns>
        /// <exception cref="ArgumentNullException">The zone is null.</exception>
        public static DateTime ToUtcLenient(DateTime local, TimeZoneInfo zone)
        {
            ArgumentNullException.ThrowIfNull(zone);
            DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            TimeSpan offset;
            if (zone.IsInvalidTime(unspecified))
            {
                offset = zone.GetUtcOffset(DateTime.SpecifyKind(unspecified.AddHours(-6), DateTimeKind.Unspecified));
            }
            else if (zone.IsAmbiguousTime(unspecified))
            {
                TimeSpan[] offsets = zone.GetAmbiguousTimeOffsets(unspecified);
                offset = offsets[0];
                foreach (TimeSpan o in offsets) if (o > offset) offset = o;
            }
            else
            {
                offset = zone.GetUtcOffset(unspecified);
            }
            return DateTime.SpecifyKind(unspecified - offset, DateTimeKind.Utc);
        }

        /// <summary>
        /// The local wall time of a UTC instant.
        /// </summary>
        /// <param name="utc">UTC instant.</param>
        /// <param name="zone">Zone.</param>
        /// <returns>Local time (unspecified kind).</returns>
        /// <exception cref="ArgumentNullException">The zone is null.</exception>
        public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone)
        {
            ArgumentNullException.ThrowIfNull(zone);
            DateTime u = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(u, zone), DateTimeKind.Unspecified);
        }

        /// <summary>
        /// Format a local time in the wire format.
        /// </summary>
        /// <param name="local">Local time.</param>
        /// <returns>yyyy-MM-ddTHH:mm.</returns>
        public static string Format(DateTime local)
        {
            return local.ToString(LocalFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A friendly local time for emails and notifications, for example Saturday, December 5 at 6:00 PM.
        /// </summary>
        /// <param name="utc">UTC instant.</param>
        /// <param name="zoneId">IANA zone.</param>
        /// <param name="isAllDay">All-day event (date only).</param>
        /// <returns>Text.</returns>
        public static string Friendly(DateTime utc, string zoneId, bool isAllDay)
        {
            TimeZoneInfo zone = Find(zoneId) ?? TimeZoneInfo.Utc;
            DateTime local = ToLocal(utc, zone);
            return isAllDay
                ? local.ToString("dddd, MMMM d", CultureInfo.InvariantCulture)
                : local.ToString("dddd, MMMM d 'at' h:mm tt", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 400 InvalidSchedule with a field error.
        /// </summary>
        /// <param name="field">Field.</param>
        /// <param name="message">Message.</param>
        /// <returns>Exception.</returns>
        public static ApiErrorException Invalid(string field, string message)
        {
            return new ApiErrorException(400, "InvalidSchedule", message, new System.Collections.Generic.Dictionary<string, string> { { field, message } });
        }

        #endregion
    }
}
