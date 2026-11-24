namespace TrellisKit.Server.Services.Jobs
{
    using System;
    using System.Linq;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Next-occurrence arithmetic of job schedules (UTC). Thread safety: stateless.
    /// </summary>
    public static class JobSchedule
    {
        #region Public-Methods

        /// <summary>
        /// The first scheduled time strictly after a moment, or null for Manual or a schedule with no days.
        /// </summary>
        /// <param name="definition">Definition.</param>
        /// <param name="afterUtc">Moment.</param>
        /// <returns>Next UTC time or null.</returns>
        /// <exception cref="ArgumentNullException">definition is null.</exception>
        public static DateTime? Next(JobDefinition definition, DateTime afterUtc)
        {
            ArgumentNullException.ThrowIfNull(definition);
            DateTime after = DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc);
            DateTime minute = new DateTime(after.Year, after.Month, after.Day, after.Hour, after.Minute, 0, DateTimeKind.Utc);
            switch (definition.IntervalType)
            {
                case JobIntervalTypeEnum.EveryNMinutes:
                    return minute.AddMinutes(Math.Clamp(definition.IntervalValue, 5, 1440));
                case JobIntervalTypeEnum.EveryNHours:
                    return minute.AddHours(Math.Clamp(definition.IntervalValue, 1, 168));
                case JobIntervalTypeEnum.EveryNDays:
                    {
                        DateTime today = after.Date.AddMinutes(definition.TimeOfDayMinutesUtc);
                        DateTime candidate = today > after ? today : today.AddDays(1);
                        if (definition.LastRunUtc != null)
                        {
                            DateTime earliest = definition.LastRunUtc.Value.Date.AddDays(Math.Clamp(definition.IntervalValue, 1, 90)).AddMinutes(definition.TimeOfDayMinutesUtc);
                            if (earliest > candidate) candidate = earliest;
                        }
                        return DateTime.SpecifyKind(candidate, DateTimeKind.Utc);
                    }
                case JobIntervalTypeEnum.DaysOfWeek:
                    {
                        if (definition.DaysOfWeek == null || definition.DaysOfWeek.Count == 0) return null;
                        for (int i = 0; i <= 7; i++)
                        {
                            DateTime candidate = after.Date.AddDays(i).AddMinutes(definition.TimeOfDayMinutesUtc);
                            if (candidate > after && definition.DaysOfWeek.Contains(candidate.DayOfWeek)) return DateTime.SpecifyKind(candidate, DateTimeKind.Utc);
                        }
                        return null;
                    }
                default:
                    return null;
            }
        }

        /// <summary>
        /// Validation message for a schedule, or null when valid.
        /// </summary>
        /// <param name="type">Type.</param>
        /// <param name="value">Interval value.</param>
        /// <param name="days">Days for DaysOfWeek.</param>
        /// <returns>Message or null.</returns>
        public static string? Problem(JobIntervalTypeEnum type, int value, System.Collections.Generic.List<DayOfWeek>? days)
        {
            switch (type)
            {
                case JobIntervalTypeEnum.EveryNMinutes:
                    return value < 5 || value > 1440 ? "Every N minutes takes 5 to 1440." : null;
                case JobIntervalTypeEnum.EveryNHours:
                    return value < 1 || value > 168 ? "Every N hours takes 1 to 168." : null;
                case JobIntervalTypeEnum.EveryNDays:
                    return value < 1 || value > 90 ? "Every N days takes 1 to 90." : null;
                case JobIntervalTypeEnum.DaysOfWeek:
                    return days == null || days.Count == 0 || days.Distinct().Count() != days.Count ? "Choose one or more distinct days." : null;
                default:
                    return null;
            }
        }

        #endregion
    }
}
