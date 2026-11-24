namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// A small RFC 5545 writer: content lines with TEXT escaping (backslash, semicolon, comma, newline), line folding
    /// at 75 octets of UTF-8 (CRLF followed by one space, never splitting a character), CRLF line ends, and VTIMEZONE
    /// components computed from the IANA rules for the years an export covers (one STANDARD or DAYLIGHT observance per
    /// transition, so the component is exact for every instant in the window). Not thread safe; use one per document.
    /// </summary>
    public class IcsWriter
    {
        #region Public-Members

        /// <summary>
        /// Maximum octets of a content line before folding (RFC 5545 section 3.1). Default 75.
        /// </summary>
        public const int MaxLineOctets = 75;

        #endregion

        #region Private-Members

        private readonly StringBuilder _Builder = new StringBuilder();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Write a raw property (the value is written as given: dates, numbers, URIs, recurrence rules).
        /// </summary>
        /// <param name="name">Property name with parameters, for example DTSTART;TZID=America/New_York.</param>
        /// <param name="value">Value.</param>
        /// <returns>This writer.</returns>
        /// <exception cref="ArgumentNullException">The name is null or empty.</exception>
        public IcsWriter Raw(string name, string value)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            AppendFolded(name + ":" + (value ?? String.Empty).Replace("\r", String.Empty).Replace("\n", String.Empty));
            return this;
        }

        /// <summary>
        /// Write a TEXT property (escaped).
        /// </summary>
        /// <param name="name">Property name.</param>
        /// <param name="value">Text.</param>
        /// <returns>This writer.</returns>
        /// <exception cref="ArgumentNullException">The name is null or empty.</exception>
        public IcsWriter Text(string name, string? value)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            AppendFolded(name + ":" + Escape(value));
            return this;
        }

        /// <summary>
        /// Begin a component (BEGIN:VEVENT).
        /// </summary>
        /// <param name="component">Component name.</param>
        /// <returns>This writer.</returns>
        public IcsWriter Begin(string component)
        {
            return Raw("BEGIN", component);
        }

        /// <summary>
        /// End a component (END:VEVENT).
        /// </summary>
        /// <param name="component">Component name.</param>
        /// <returns>This writer.</returns>
        public IcsWriter End(string component)
        {
            return Raw("END", component);
        }

        /// <summary>
        /// Write a VTIMEZONE for a zone covering a window: every UTC offset transition inside it becomes an observance
        /// whose DTSTART is the local time before the transition; a zone without transitions gets one STANDARD
        /// observance.
        /// </summary>
        /// <param name="zone">Zone.</param>
        /// <param name="fromUtc">Window start.</param>
        /// <param name="toUtc">Window end.</param>
        /// <returns>This writer.</returns>
        /// <exception cref="ArgumentNullException">The zone is null.</exception>
        public IcsWriter TimeZone(TimeZoneInfo zone, DateTime fromUtc, DateTime toUtc)
        {
            ArgumentNullException.ThrowIfNull(zone);
            DateTime start = new DateTime(fromUtc.Year - 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime end = new DateTime(Math.Min(9998, toUtc.Year + 1), 12, 31, 0, 0, 0, DateTimeKind.Utc);
            Begin("VTIMEZONE");
            Raw("TZID", zone.Id);
            List<DateTime> transitions = Transitions(zone, start, end);
            if (transitions.Count == 0)
            {
                TimeSpan offset = zone.GetUtcOffset(start);
                Begin("STANDARD");
                Raw("DTSTART", "19700101T000000");
                Raw("TZOFFSETFROM", Offset(offset));
                Raw("TZOFFSETTO", Offset(offset));
                Raw("TZNAME", Name(zone, start));
                End("STANDARD");
            }
            else
            {
                foreach (DateTime t in transitions)
                {
                    TimeSpan before = zone.GetUtcOffset(t.AddSeconds(-1));
                    TimeSpan after = zone.GetUtcOffset(t);
                    bool daylight = zone.IsDaylightSavingTime(t);
                    string kind = daylight ? "DAYLIGHT" : "STANDARD";
                    Begin(kind);
                    Raw("DTSTART", LocalStamp(DateTime.SpecifyKind(t + before, DateTimeKind.Unspecified)));
                    Raw("TZOFFSETFROM", Offset(before));
                    Raw("TZOFFSETTO", Offset(after));
                    Raw("TZNAME", Name(zone, t));
                    End(kind);
                }
            }
            End("VTIMEZONE");
            return this;
        }

        /// <summary>
        /// The document text (CRLF line ends).
        /// </summary>
        /// <returns>Text.</returns>
        public override string ToString()
        {
            return _Builder.ToString();
        }

        /// <summary>
        /// Escape a TEXT value (RFC 5545 section 3.3.11).
        /// </summary>
        /// <param name="value">Text.</param>
        /// <returns>Escaped text.</returns>
        public static string Escape(string? value)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            StringBuilder b = new StringBuilder(value.Length + 8);
            string normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (char c in normalized)
            {
                switch (c)
                {
                    case '\\': b.Append("\\\\"); break;
                    case ';': b.Append("\\;"); break;
                    case ',': b.Append("\\,"); break;
                    case '\n': b.Append("\\n"); break;
                    default:
                        if (c < 0x20 && c != '\t') continue;
                        b.Append(c);
                        break;
                }
            }
            return b.ToString();
        }

        /// <summary>
        /// A UTC DATE-TIME value, for example 20261205T180000Z.
        /// </summary>
        /// <param name="utc">UTC instant.</param>
        /// <returns>Value.</returns>
        public static string UtcStamp(DateTime utc)
        {
            DateTime u = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return u.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A local (floating or TZID) DATE-TIME value, for example 20261205T100000.
        /// </summary>
        /// <param name="local">Local time.</param>
        /// <returns>Value.</returns>
        public static string LocalStamp(DateTime local)
        {
            return local.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A DATE value, for example 20261205.
        /// </summary>
        /// <param name="date">Date.</param>
        /// <returns>Value.</returns>
        public static string DateStamp(DateTime date)
        {
            return date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Fold one content line (without its line end) into lines of at most 75 octets, each continuation starting
        /// with one space.
        /// </summary>
        /// <param name="line">Content line.</param>
        /// <returns>Folded lines.</returns>
        public static List<string> Fold(string line)
        {
            List<string> result = new List<string>();
            StringBuilder current = new StringBuilder();
            int octets = 0;
            int limit = MaxLineOctets;
            int i = 0;
            while (i < line.Length)
            {
                int length = Char.IsHighSurrogate(line[i]) && i + 1 < line.Length ? 2 : 1;
                string unit = line.Substring(i, length);
                int size = Encoding.UTF8.GetByteCount(unit);
                if (octets + size > limit)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    current.Append(' ');
                    octets = 1;
                }
                current.Append(unit);
                octets += size;
                i += length;
            }
            result.Add(current.ToString());
            return result;
        }

        #endregion

        #region Private-Methods

        private void AppendFolded(string line)
        {
            foreach (string part in Fold(line))
            {
                _Builder.Append(part);
                _Builder.Append("\r\n");
            }
        }

        private static List<DateTime> Transitions(TimeZoneInfo zone, DateTime start, DateTime end)
        {
            List<DateTime> result = new List<DateTime>();
            DateTime cursor = start;
            TimeSpan offset = zone.GetUtcOffset(cursor);
            while (cursor < end)
            {
                DateTime next = cursor.AddDays(1);
                TimeSpan nextOffset = zone.GetUtcOffset(next);
                if (nextOffset != offset)
                {
                    DateTime low = cursor;
                    DateTime high = next;
                    while ((high - low).TotalMinutes > 1)
                    {
                        DateTime mid = low.AddTicks((high - low).Ticks / 2);
                        if (zone.GetUtcOffset(mid) == offset) low = mid;
                        else high = mid;
                    }
                    DateTime at = new DateTime(high.Year, high.Month, high.Day, high.Hour, high.Minute, 0, DateTimeKind.Utc);
                    if (zone.GetUtcOffset(at) == offset) at = at.AddMinutes(1);
                    result.Add(at);
                    offset = nextOffset;
                }
                cursor = next;
            }
            return result;
        }

        private static string Offset(TimeSpan offset)
        {
            string sign = offset < TimeSpan.Zero ? "-" : "+";
            TimeSpan a = offset.Duration();
            return sign + a.Hours.ToString("00", CultureInfo.InvariantCulture) + a.Minutes.ToString("00", CultureInfo.InvariantCulture);
        }

        private static string Name(TimeZoneInfo zone, DateTime utc)
        {
            TimeSpan offset = zone.GetUtcOffset(utc);
            return offset == TimeSpan.Zero ? "UTC" : "UTC" + Offset(offset);
        }

        #endregion
    }
}
