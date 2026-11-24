namespace TrellisKit.Server.Services.Usage
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;

    /// <summary>
    /// Resolves usage periods into exact bounds and buckets in a property's time zone (docs/USAGE_METERING_CONTRACT.md
    /// sections 3.2 and 3.3). Pure and thread-safe.
    /// </summary>
    public static class UsagePeriodCalculator
    {
        #region Public-Members

        /// <summary>
        /// Most buckets in a series. 400.
        /// </summary>
        public const int MaxBuckets = 400;

        /// <summary>
        /// Longest range in days. 1830 (5 years).
        /// </summary>
        public const int MaxRangeDays = 1830;

        /// <summary>
        /// Longest period for Hour buckets in days. 14.
        /// </summary>
        public const int MaxHourDays = 14;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse a period name (case-insensitive).
        /// </summary>
        /// <param name="text">Period name, or null for LastMonth.</param>
        /// <param name="errors">Field errors (period).</param>
        /// <returns>Period.</returns>
        public static UsagePeriodEnum ParsePeriod(string? text, Dictionary<string, string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            if (String.IsNullOrWhiteSpace(text)) return UsagePeriodEnum.LastMonth;
            if (Enum.TryParse(text.Trim(), true, out UsagePeriodEnum period) && Enum.IsDefined(period)) return period;
            errors["period"] = "Must be one of: " + String.Join(", ", Enum.GetNames<UsagePeriodEnum>()) + ".";
            return UsagePeriodEnum.LastMonth;
        }

        /// <summary>
        /// Parse a bucket name (case-insensitive).
        /// </summary>
        /// <param name="text">Bucket name, or null for the default.</param>
        /// <param name="errors">Field errors (bucket).</param>
        /// <returns>Bucket, or null.</returns>
        public static UsageBucketEnum? ParseBucket(string? text, Dictionary<string, string> errors)
        {
            ArgumentNullException.ThrowIfNull(errors);
            if (String.IsNullOrWhiteSpace(text)) return null;
            if (Enum.TryParse(text.Trim(), true, out UsageBucketEnum bucket) && Enum.IsDefined(bucket)) return bucket;
            errors["bucket"] = "Must be one of: " + String.Join(", ", Enum.GetNames<UsageBucketEnum>()) + ".";
            return null;
        }

        /// <summary>
        /// Resolve a period.
        /// </summary>
        /// <param name="period">Period.</param>
        /// <param name="from">Range start (local date yyyy-MM-dd or ISO 8601 date-time with offset); Range only.</param>
        /// <param name="to">Range end (local date, inclusive, or date-time, exclusive); Range only.</param>
        /// <param name="bucket">Bucket override, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="zone">Property calendar.</param>
        /// <param name="hourlyRetentionDays">Hourly retention (Hour buckets only start inside it).</param>
        /// <returns>Window.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed (period, from, to, bucket).</exception>
        public static UsageWindow Resolve(UsagePeriodEnum period, string? from, string? to, UsageBucketEnum? bucket, DateTime nowUtc, UsageZone zone, int hourlyRetentionDays)
        {
            ArgumentNullException.ThrowIfNull(zone);
            DateTime now = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
            DateOnly today = zone.LocalDate(now);
            UsageWindow w = new UsageWindow { Period = period, ToUtc = now };
            switch (period)
            {
                case UsagePeriodEnum.LastDay:
                    w.FromUtc = FloorHour(now).AddHours(-23);
                    w.PreviousFromUtc = w.FromUtc.AddHours(-24);
                    w.PreviousToUtc = now.AddHours(-24);
                    break;
                case UsagePeriodEnum.LastWeek:
                    Trailing(w, zone, today, now, 7);
                    break;
                case UsagePeriodEnum.LastMonth:
                    Trailing(w, zone, today, now, 30);
                    break;
                case UsagePeriodEnum.Last90Days:
                    Trailing(w, zone, today, now, 90);
                    break;
                case UsagePeriodEnum.PreviousQuarter:
                    DateOnly quarter = new DateOnly(today.Year, ((today.Month - 1) / 3) * 3 + 1, 1);
                    w.FromUtc = zone.Midnight(quarter.AddMonths(-3));
                    w.ToUtc = zone.Midnight(quarter);
                    w.PreviousFromUtc = zone.Midnight(quarter.AddMonths(-6));
                    w.PreviousToUtc = w.FromUtc;
                    break;
                case UsagePeriodEnum.LastYear:
                    Trailing(w, zone, today, now, 365);
                    break;
                case UsagePeriodEnum.ThisWeek:
                    DateOnly weekStart = zone.WeekStartOf(today);
                    w.FromUtc = zone.Midnight(weekStart);
                    w.PreviousFromUtc = zone.Midnight(weekStart.AddDays(-7));
                    w.PreviousToUtc = Min(zone.ShiftDays(now, -7), w.FromUtc);
                    break;
                case UsagePeriodEnum.ThisMonth:
                    ToDate(w, zone, now, new DateOnly(today.Year, today.Month, 1), 1);
                    break;
                case UsagePeriodEnum.ThisQuarter:
                    ToDate(w, zone, now, new DateOnly(today.Year, ((today.Month - 1) / 3) * 3 + 1, 1), 3);
                    break;
                case UsagePeriodEnum.ThisYear:
                    ToDate(w, zone, now, new DateOnly(today.Year, 1, 1), 12);
                    break;
                default:
                    Range(w, zone, now, from, to);
                    break;
            }
            w.Bucket = ChooseBucket(w, bucket, now, hourlyRetentionDays);
            w.Buckets = Buckets(w.FromUtc, w.ToUtc, w.Bucket, zone);
            if (w.Buckets.Count > MaxBuckets) throw ApiErrorException.Field("bucket", "At most " + MaxBuckets + " buckets; choose a larger bucket.");
            return w;
        }

        /// <summary>
        /// Build the buckets of [fromUtc, toUtc).
        /// </summary>
        /// <param name="fromUtc">Start.</param>
        /// <param name="toUtc">End.</param>
        /// <param name="bucket">Bucket size.</param>
        /// <param name="zone">Calendar.</param>
        /// <returns>Buckets in time order (stops after MaxBuckets plus one).</returns>
        public static List<UsageWindowBucket> Buckets(DateTime fromUtc, DateTime toUtc, UsageBucketEnum bucket, UsageZone zone)
        {
            ArgumentNullException.ThrowIfNull(zone);
            List<UsageWindowBucket> list = new List<UsageWindowBucket>();
            if (toUtc <= fromUtc) return list;
            if (bucket == UsageBucketEnum.Hour)
            {
                for (DateTime t = FloorHour(fromUtc); t < toUtc && list.Count <= MaxBuckets; t = t.AddHours(1))
                    list.Add(Make(t, t.AddHours(1), fromUtc, toUtc, zone.Local(t).ToString("yyyy-MM-dd'T'HH':00'", CultureInfo.InvariantCulture)));
                return list;
            }
            DateOnly day = zone.LocalDate(fromUtc);
            if (bucket == UsageBucketEnum.Week) day = zone.WeekStartOf(day);
            if (bucket == UsageBucketEnum.Month) day = new DateOnly(day.Year, day.Month, 1);
            while (list.Count <= MaxBuckets)
            {
                DateTime start = zone.Midnight(day);
                if (start >= toUtc) break;
                DateOnly next = bucket == UsageBucketEnum.Day ? day.AddDays(1) : bucket == UsageBucketEnum.Week ? day.AddDays(7) : day.AddMonths(1);
                string label = bucket == UsageBucketEnum.Month ? day.ToString("yyyy-MM", CultureInfo.InvariantCulture) : day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                list.Add(Make(start, zone.Midnight(next), fromUtc, toUtc, label));
                day = next;
            }
            return list;
        }

        /// <summary>
        /// The start of the UTC hour containing an instant.
        /// </summary>
        /// <param name="utc">Instant.</param>
        /// <returns>Hour start (UTC).</returns>
        public static DateTime FloorHour(DateTime utc)
        {
            return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
        }

        #endregion

        #region Private-Methods

        private static UsageWindowBucket Make(DateTime start, DateTime end, DateTime fromUtc, DateTime toUtc, string label)
        {
            return new UsageWindowBucket
            {
                Key = start.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                Label = label,
                SelectStartUtc = start,
                SelectEndUtc = end,
                StartUtc = start < fromUtc ? fromUtc : start,
                EndUtc = end > toUtc ? toUtc : end
            };
        }

        private static void Trailing(UsageWindow w, UsageZone zone, DateOnly today, DateTime now, int days)
        {
            w.FromUtc = zone.Midnight(today.AddDays(1 - days));
            w.PreviousFromUtc = zone.Midnight(today.AddDays(1 - 2 * days));
            w.PreviousToUtc = Min(zone.ShiftDays(now, -days), w.FromUtc);
        }

        private static void ToDate(UsageWindow w, UsageZone zone, DateTime now, DateOnly start, int months)
        {
            w.FromUtc = zone.Midnight(start);
            w.PreviousFromUtc = zone.Midnight(start.AddMonths(-months));
            DateTime local = zone.Local(now);
            DateTime previousLocal = local.AddMonths(-months);
            w.PreviousToUtc = Min(zone.ToUtc(previousLocal), w.FromUtc);
        }

        private static void Range(UsageWindow w, UsageZone zone, DateTime now, string? from, string? to)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (String.IsNullOrWhiteSpace(from)) errors["from"] = "Required for period Range.";
            if (String.IsNullOrWhiteSpace(to)) errors["to"] = "Required for period Range.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "A range needs from and to.");
            bool fromDate = TryDate(from!, out DateOnly fromDay);
            bool toDate = TryDate(to!, out DateOnly toDay);
            DateTime start;
            DateTime end;
            if (fromDate) start = zone.Midnight(fromDay);
            else if (!TryInstant(from!, out start)) errors["from"] = "Must be a date (yyyy-MM-dd) or an ISO 8601 date-time with an offset.";
            if (toDate) end = zone.Midnight(toDay.AddDays(1));
            else if (!TryInstant(to!, out end)) errors["to"] = "Must be a date (yyyy-MM-dd) or an ISO 8601 date-time with an offset.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "The range is invalid.");
            if (end <= start) throw ApiErrorException.Field("to", "Must be after from.");
            if (start > now) throw ApiErrorException.Field("from", "Must not be in the future.");
            if ((end - start).TotalDays > MaxRangeDays + 1) throw ApiErrorException.Field("to", "A range spans at most " + MaxRangeDays + " days.");
            w.FromUtc = start;
            w.ToUtc = end > now ? now : end;
            if (fromDate && toDate)
            {
                int days = toDay.DayNumber - fromDay.DayNumber + 1;
                w.PreviousFromUtc = zone.Midnight(fromDay.AddDays(-days));
                w.PreviousToUtc = Min(zone.ShiftDays(w.ToUtc, -days), start);
            }
            else
            {
                TimeSpan length = w.ToUtc - start;
                w.PreviousFromUtc = start - length;
                w.PreviousToUtc = start;
            }
        }

        private static UsageBucketEnum ChooseBucket(UsageWindow w, UsageBucketEnum? requested, DateTime now, int hourlyRetentionDays)
        {
            double days = (w.ToUtc - w.FromUtc).TotalDays;
            bool hourlyAvailable = w.FromUtc >= FloorHour(now).AddDays(-Math.Max(1, hourlyRetentionDays));
            if (requested != null)
            {
                if (requested == UsageBucketEnum.Hour)
                {
                    if (days > MaxHourDays) throw ApiErrorException.Field("bucket", "Hour buckets cover at most " + MaxHourDays + " days.");
                    if (!hourlyAvailable) throw ApiErrorException.Field("bucket", "Hour buckets are kept for " + hourlyRetentionDays + " days.");
                }
                return requested.Value;
            }
            if (days <= 2.0001 && hourlyAvailable) return UsageBucketEnum.Hour;
            if (days <= 93) return UsageBucketEnum.Day;
            if (days <= 400) return UsageBucketEnum.Week;
            return UsageBucketEnum.Month;
        }

        private static bool TryDate(string text, out DateOnly date)
        {
            return DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private static bool TryInstant(string text, out DateTime utc)
        {
            utc = DateTime.MinValue;
            string t = text.Trim();
            if (!t.Contains('T', StringComparison.Ordinal)) return false;
            bool hasOffset = t.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(t, "[+-][0-9]{2}:?[0-9]{2}$");
            if (!hasOffset) return false;
            if (!DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset value)) return false;
            utc = value.UtcDateTime;
            return true;
        }

        private static DateTime Min(DateTime a, DateTime b)
        {
            return a < b ? a : b;
        }

        #endregion
    }
}
