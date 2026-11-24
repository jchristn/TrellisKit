namespace TrellisKit.Server.Services
{
    using System;
    using System.Globalization;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Notification hours (Phase 2 contract 8.6): whether a time is inside a member's push window, plus HH:mm and IANA
    /// time zone parsing. Disabled, or start equal to end, means all day; a start after the end crosses midnight. A
    /// missing or unknown time zone is evaluated as UTC.
    /// Thread safety: stateless.
    /// </summary>
    public static class NotificationHoursRule
    {
        /// <summary>
        /// Whether pushes may arrive at a time.
        /// </summary>
        /// <param name="hours">Member's hours.</param>
        /// <param name="nowUtc">Time (UTC).</param>
        /// <returns>True inside the window.</returns>
        /// <exception cref="ArgumentNullException">The hours are null.</exception>
        public static bool IsWithin(NotificationHours hours, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(hours);
            if (!hours.Enabled || hours.StartMinute == hours.EndMinute) return true;
            DateTime local = ToLocal(nowUtc, hours.TimeZone);
            int minute = local.Hour * 60 + local.Minute;
            if (hours.StartMinute < hours.EndMinute) return minute >= hours.StartMinute && minute < hours.EndMinute;
            return minute >= hours.StartMinute || minute < hours.EndMinute;
        }

        /// <summary>
        /// Parse HH:mm (00:00 to 23:59).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Minutes after midnight, or null when malformed.</returns>
        public static int? ParseMinute(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            string value = text.Trim();
            if (value.Length != 5 || value[2] != ':') return null;
            if (!Int32.TryParse(value.Substring(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int hour)) return null;
            if (!Int32.TryParse(value.Substring(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int minute)) return null;
            if (hour > 23 || minute > 59) return null;
            return hour * 60 + minute;
        }

        /// <summary>
        /// Format minutes after midnight as HH:mm.
        /// </summary>
        /// <param name="minute">Minutes (0 to 1439).</param>
        /// <returns>Text.</returns>
        public static string FormatMinute(int minute)
        {
            int clamped = Math.Clamp(minute, 0, 1439);
            return (clamped / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (clamped % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Whether a value is an IANA time zone identifier known to the runtime (Windows identifiers are rejected).
        /// </summary>
        /// <param name="timeZone">Identifier.</param>
        /// <returns>True when valid.</returns>
        public static bool IsIanaTimeZone(string? timeZone)
        {
            if (String.IsNullOrWhiteSpace(timeZone)) return false;
            string id = timeZone.Trim();
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(id, out TimeZoneInfo? _)) return false;
            if (String.Equals(id, "UTC", StringComparison.Ordinal) || String.Equals(id, "Etc/UTC", StringComparison.Ordinal)) return true;
            return TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out string? _);
        }

        private static DateTime ToLocal(DateTime nowUtc, string? timeZone)
        {
            DateTime utc = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
            if (String.IsNullOrWhiteSpace(timeZone) || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone.Trim(), out TimeZoneInfo? zone)) return utc;
            return TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        }
    }
}
