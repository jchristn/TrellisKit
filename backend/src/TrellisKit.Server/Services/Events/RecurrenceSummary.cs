namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Plain English summaries of recurrence rules, for example "Every week on Tuesday and Thursday, 12 times" or
    /// "Every month on the last Friday, until June 30, 2027". Thread safety: stateless.
    /// </summary>
    public static class RecurrenceSummary
    {
        #region Private-Members

        private static readonly string[] _Ordinals = new[] { "first", "second", "third", "fourth" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Describe a rule.
        /// </summary>
        /// <param name="rule">Rule.</param>
        /// <param name="anchor">Local start of the series (default weekday and month day).</param>
        /// <returns>Summary.</returns>
        /// <exception cref="ArgumentNullException">The rule is null.</exception>
        public static string Describe(EventRecurrenceRule rule, DateTime anchor)
        {
            ArgumentNullException.ThrowIfNull(rule);
            int n = Math.Max(1, rule.Interval);
            string text;
            switch (rule.Frequency)
            {
                case EventRecurrenceFrequencyEnum.Daily:
                    text = n == 1 ? "Every day" : "Every " + n.ToString(CultureInfo.InvariantCulture) + " days";
                    break;
                case EventRecurrenceFrequencyEnum.Weekly:
                    {
                        List<DayOfWeek> days = rule.Weekdays.Count == 0 ? new List<DayOfWeek> { anchor.DayOfWeek } : rule.Weekdays.Distinct().OrderBy(d => ((int)d + 6) % 7).ToList();
                        text = (n == 1 ? "Every week" : "Every " + n.ToString(CultureInfo.InvariantCulture) + " weeks") + " on " + Join(days.Select(d => d.ToString()).ToList());
                        break;
                    }
                case EventRecurrenceFrequencyEnum.Monthly:
                    {
                        string every = n == 1 ? "Every month" : "Every " + n.ToString(CultureInfo.InvariantCulture) + " months";
                        if (rule.SetPosition != null && rule.Weekdays.Count > 0)
                        {
                            string ordinal = rule.SetPosition.Value == -1 ? "last" : _Ordinals[Math.Clamp(rule.SetPosition.Value, 1, 4) - 1];
                            text = every + " on the " + ordinal + " " + rule.Weekdays[0];
                        }
                        else
                        {
                            int day = rule.MonthDay ?? anchor.Day;
                            text = day == -1 ? every + " on the last day" : every + " on day " + day.ToString(CultureInfo.InvariantCulture);
                        }
                        break;
                    }
                default:
                    text = (n == 1 ? "Every year" : "Every " + n.ToString(CultureInfo.InvariantCulture) + " years") + " on " + anchor.ToString("MMMM d", CultureInfo.InvariantCulture);
                    break;
            }
            if (rule.Count != null) text += rule.Count.Value == 1 ? ", once" : ", " + rule.Count.Value.ToString(CultureInfo.InvariantCulture) + " times";
            else if (rule.UntilDate != null) text += ", until " + rule.UntilDate.Value.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
            return text;
        }

        #endregion

        #region Private-Methods

        private static string Join(List<string> items)
        {
            if (items.Count == 0) return String.Empty;
            if (items.Count == 1) return items[0];
            if (items.Count == 2) return items[0] + " and " + items[1];
            return String.Join(", ", items.Take(items.Count - 1)) + ", and " + items[items.Count - 1];
        }

        #endregion
    }
}
