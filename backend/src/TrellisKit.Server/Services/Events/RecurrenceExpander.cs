namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Expands a recurrence rule into occurrences (resolved decision 11). Occurrences keep the local wall time of the
    /// first occurrence in the event's zone, so their UTC instants shift across DST transitions; a wall time that does
    /// not exist on a transition day moves forward by the gap, and an ambiguous one takes the first instant. The first
    /// occurrence is the series start; weeks start on Monday (RFC 5545 WKST=MO); a monthly or yearly date that does not
    /// exist in a period (the 31st in April, February 29) is skipped, as RFC 5545 requires (use monthDay -1 for the last
    /// day). Thread safety: stateless.
    /// </summary>
    public static class RecurrenceExpander
    {
        #region Public-Members

        /// <summary>
        /// Upper bound of periods scanned in one expansion (guards against rules that never match).
        /// </summary>
        public const int MaxPeriods = 20000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The occurrences of a rule from its start, in order, until the count, the until date, the horizon, or the
        /// limit is reached.
        /// </summary>
        /// <param name="rule">Rule.</param>
        /// <param name="startLocal">Local start of the first occurrence.</param>
        /// <param name="zone">Zone.</param>
        /// <param name="durationMinutes">Duration of each occurrence.</param>
        /// <param name="horizonUtc">Stop at occurrences starting after this instant.</param>
        /// <param name="limit">Most occurrences returned.</param>
        /// <returns>Occurrences with their indexes.</returns>
        /// <exception cref="ArgumentNullException">The rule or zone is null.</exception>
        public static List<EventOccurrenceSlot> Expand(EventRecurrenceRule rule, DateTime startLocal, TimeZoneInfo zone, int durationMinutes, DateTime horizonUtc, int limit)
        {
            ArgumentNullException.ThrowIfNull(rule);
            ArgumentNullException.ThrowIfNull(zone);
            List<EventOccurrenceSlot> result = new List<EventOccurrenceSlot>();
            int index = 0;
            foreach (DateTime date in Dates(rule, startLocal.Date))
            {
                if (rule.Count != null && index >= rule.Count.Value) break;
                if (rule.UntilDate != null && date > rule.UntilDate.Value.Date) break;
                DateTime local = DateTime.SpecifyKind(date + startLocal.TimeOfDay, DateTimeKind.Unspecified);
                DateTime startUtc = EventTimeZones.ToUtcLenient(local, zone);
                if (startUtc > horizonUtc) break;
                DateTime endLocal = local.AddMinutes(durationMinutes);
                DateTime endUtc = EventTimeZones.ToUtcLenient(endLocal, zone);
                if (endUtc <= startUtc) endUtc = startUtc.AddMinutes(Math.Max(1, durationMinutes));
                result.Add(new EventOccurrenceSlot { Index = index, StartLocal = local, StartUtc = startUtc, EndUtc = endUtc });
                index++;
                if (result.Count >= limit) break;
            }
            return result;
        }

        /// <summary>
        /// Whether a local date is produced by a rule anchored at a start date (on or after it).
        /// </summary>
        /// <param name="rule">Rule.</param>
        /// <param name="anchor">Start date of the series.</param>
        /// <param name="date">Date to test.</param>
        /// <returns>True when the rule produces the date (count and until are ignored).</returns>
        /// <exception cref="ArgumentNullException">The rule is null.</exception>
        public static bool Matches(EventRecurrenceRule rule, DateTime anchor, DateTime date)
        {
            ArgumentNullException.ThrowIfNull(rule);
            DateTime d = date.Date;
            DateTime a = anchor.Date;
            if (d < a) return false;
            int interval = Math.Max(1, rule.Interval);
            switch (rule.Frequency)
            {
                case EventRecurrenceFrequencyEnum.Daily:
                    return (d - a).Days % interval == 0;
                case EventRecurrenceFrequencyEnum.Weekly:
                    {
                        List<DayOfWeek> days = WeeklyDays(rule, a);
                        if (!days.Contains(d.DayOfWeek)) return false;
                        int weeks = (Monday(d) - Monday(a)).Days / 7;
                        return weeks % interval == 0;
                    }
                case EventRecurrenceFrequencyEnum.Monthly:
                    {
                        int months = (d.Year - a.Year) * 12 + d.Month - a.Month;
                        if (months % interval != 0) return false;
                        DateTime? day = MonthDate(rule, a, d.Year, d.Month);
                        return day != null && day.Value == d;
                    }
                default:
                    {
                        int years = d.Year - a.Year;
                        if (years % interval != 0) return false;
                        return d.Month == a.Month && d.Day == a.Day;
                    }
            }
        }

        /// <summary>
        /// The first date on or after a date that a rule anchored there produces, or null within the scan bound.
        /// </summary>
        /// <param name="rule">Rule.</param>
        /// <param name="from">First candidate date.</param>
        /// <returns>Date or null.</returns>
        /// <exception cref="ArgumentNullException">The rule is null.</exception>
        public static DateTime? FirstOnOrAfter(EventRecurrenceRule rule, DateTime from)
        {
            ArgumentNullException.ThrowIfNull(rule);
            DateTime d = from.Date;
            for (int i = 0; i < 3700; i++)
            {
                DateTime candidate = d.AddDays(i);
                if (Fits(rule, candidate)) return candidate;
            }
            return null;
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<DateTime> Dates(EventRecurrenceRule rule, DateTime anchor)
        {
            int interval = Math.Max(1, rule.Interval);
            switch (rule.Frequency)
            {
                case EventRecurrenceFrequencyEnum.Daily:
                    for (int i = 0; i < MaxPeriods; i++) yield return anchor.AddDays((long)i * interval);
                    yield break;
                case EventRecurrenceFrequencyEnum.Weekly:
                    {
                        List<DayOfWeek> days = WeeklyDays(rule, anchor).OrderBy(d => ((int)d + 6) % 7).ToList();
                        DateTime week = Monday(anchor);
                        for (int i = 0; i < MaxPeriods; i++)
                        {
                            DateTime start = week.AddDays(7L * i * interval);
                            foreach (DayOfWeek day in days)
                            {
                                DateTime date = start.AddDays(((int)day + 6) % 7);
                                if (date >= anchor) yield return date;
                            }
                        }
                        yield break;
                    }
                case EventRecurrenceFrequencyEnum.Monthly:
                    for (int i = 0; i < MaxPeriods; i++)
                    {
                        DateTime month = new DateTime(anchor.Year, anchor.Month, 1).AddMonths(i * interval);
                        if (month.Year > 9000) yield break;
                        DateTime? date = MonthDate(rule, anchor, month.Year, month.Month);
                        if (date != null && date.Value >= anchor) yield return date.Value;
                    }
                    yield break;
                default:
                    for (int i = 0; i < MaxPeriods; i++)
                    {
                        int year = anchor.Year + i * interval;
                        if (year > 9000) yield break;
                        if (anchor.Month == 2 && anchor.Day == 29 && !DateTime.IsLeapYear(year)) continue;
                        yield return new DateTime(year, anchor.Month, anchor.Day);
                    }
                    yield break;
            }
        }

        private static bool Fits(EventRecurrenceRule rule, DateTime date)
        {
            switch (rule.Frequency)
            {
                case EventRecurrenceFrequencyEnum.Weekly:
                    return rule.Weekdays.Count == 0 || rule.Weekdays.Contains(date.DayOfWeek);
                case EventRecurrenceFrequencyEnum.Monthly:
                    {
                        DateTime? d = MonthDate(rule, date, date.Year, date.Month);
                        return d != null && d.Value == date.Date;
                    }
                default:
                    return true;
            }
        }

        private static List<DayOfWeek> WeeklyDays(EventRecurrenceRule rule, DateTime anchor)
        {
            List<DayOfWeek> days = rule.Weekdays.Distinct().ToList();
            if (days.Count == 0) days.Add(anchor.DayOfWeek);
            return days;
        }

        private static DateTime Monday(DateTime date)
        {
            int back = ((int)date.DayOfWeek + 6) % 7;
            return date.Date.AddDays(-back);
        }

        private static DateTime? MonthDate(EventRecurrenceRule rule, DateTime anchor, int year, int month)
        {
            int days = DateTime.DaysInMonth(year, month);
            if (rule.SetPosition != null && rule.Weekdays.Count > 0)
            {
                DayOfWeek weekday = rule.Weekdays[0];
                if (rule.SetPosition.Value == -1)
                {
                    DateTime last = new DateTime(year, month, days);
                    while (last.DayOfWeek != weekday) last = last.AddDays(-1);
                    return last;
                }
                DateTime first = new DateTime(year, month, 1);
                while (first.DayOfWeek != weekday) first = first.AddDays(1);
                DateTime nth = first.AddDays(7 * (rule.SetPosition.Value - 1));
                return nth.Month == month ? nth : null;
            }
            int monthDay = rule.MonthDay ?? anchor.Day;
            if (monthDay == -1) return new DateTime(year, month, days);
            if (monthDay < 1 || monthDay > days) return null;
            return new DateTime(year, month, monthDay);
        }

        #endregion
    }
}
