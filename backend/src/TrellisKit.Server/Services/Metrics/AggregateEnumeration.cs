namespace TrellisKit.Server.Services.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>Page independently bounded aggregate observations through the common enumeration contract.</summary>
    public static class AggregateEnumeration
    {
        /// <summary>Create an observation collection with empty metadata and explicit custom filter names.</summary>
        public static EnumerationCollection Collection(string name, params string[] filters)
        {
            return new EnumerationCollection(name, null, "id", "createdutc", true) { HasInlineMetadata = true }.Filters(filters);
        }

        /// <summary>Reject nested filter values rather than silently ignoring them.</summary>
        public static void ValidateScalarFilters(EnumerationQuery query)
        {
            foreach (KeyValuePair<string, JsonElement> filter in query.Filters)
            {
                if (filter.Value.ValueKind == JsonValueKind.Array || filter.Value.ValueKind == JsonValueKind.Object)
                    throw ApiErrorException.Validation(new Dictionary<string, string> { ["filters." + filter.Key] = "Use a scalar filter value." }, "Invalid aggregate filter.");
            }
        }

        /// <summary>Read a bounded, typed days selection.</summary>
        public static int Days(EnumerationQuery query, int minimum = 7, int maximum = 365, int fallback = 30)
        {
            ValidateScalarFilters(query);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            long days = query.FilterLong("days", errors) ?? fallback;
            if (days < minimum || days > maximum) errors["filters.days"] = $"Must be {minimum} to {maximum}.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "Invalid aggregate window.");
            return (int)days;
        }

        /// <summary>Enumerate each daily observation, retaining scalar context and explicit bounded previews.</summary>
        public static CatalogEnumerationResponse<LegacyMetricRow> Daily(ApplicationServices services, string name, EnumerationQuery query, object summary)
        {
            ValidateScalarFilters(query);
            EnumerationRequest request = services.Enumeration.Prepare(query, Collection(name, "days", "series"), true);
            List<LegacyMetricRow> rows = new List<LegacyMetricRow>();
            Dictionary<string, object?> context = new Dictionary<string, object?>();
            foreach (PropertyInfo property in summary.GetType().GetProperties())
            {
                string key = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                object? value = property.GetValue(summary);
                if (value is IEnumerable<DailyCount> daily)
                {
                    foreach (DailyCount point in daily)
                        rows.Add(new LegacyMetricRow { Id = key + ":" + point.Date, Series = key, Date = point.Date, Value = point.Count });
                }
                else if (value is IEnumerable<EventDayCountView> eventDaily)
                {
                    foreach (EventDayCountView point in eventDaily)
                        rows.Add(new LegacyMetricRow { Id = key + ":" + point.Day, Series = key, Date = point.Day, Value = point.Count });
                }
                else if (value is System.Collections.IEnumerable && value is not string)
                {
                    throw new InvalidOperationException("Aggregate collection needs its own enumeration: " + key);
                }
                else context[key] = value;
            }
            if (query.FilterString("series") is string series) rows = rows.Where(row => row.Series == series).ToList();
            EnumerationResult<LegacyMetricRow> page = CatalogEnumeration.Page(request, rows, row => row.Id,
                row => DateTime.ParseExact(row.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                row => row.Series, labels: row => row.Labels, tags: row => row.Tags);
            return CatalogEnumerationResponse<LegacyMetricRow>.From(page, context);
        }

        /// <summary>Page request-history activity buckets rather than embedding every bucket in a summary.</summary>
        public static CatalogEnumerationResponse<RequestHistoryBucket> Requests(ApplicationServices services, EnumerationQuery query, RequestHistorySummary summary)
        {
            ValidateScalarFilters(query);
            EnumerationRequest request = services.Enumeration.Prepare(query, Collection("request-history-summary", "fromUtc", "toUtc", "bucketMinutes", "method", "statusCode", "pathContains", "userId", "routeTemplate", "statusClass", "errorCode", "clientApp", "clientPlatform", "clientVersion"), true);
            EnumerationResult<RequestHistoryBucket> page = CatalogEnumeration.Page(request, summary.Buckets,
                bucket => bucket.BucketStartUtc.ToString("O", CultureInfo.InvariantCulture), bucket => bucket.BucketStartUtc,
                labels: bucket => bucket.Labels, tags: bucket => bucket.Tags);
            return CatalogEnumerationResponse<RequestHistoryBucket>.From(page, new Dictionary<string, object?>
            {
                ["totalCount"] = summary.TotalCount,
                ["totalSuccess"] = summary.TotalSuccess,
                ["totalFailure"] = summary.TotalFailure,
                ["averageDurationMs"] = summary.AverageDurationMs
            });
        }

        /// <summary>Page AI observations with scalar metric context.</summary>
        public static CatalogEnumerationResponse<AiTimeseriesPoint> Ai(ApplicationServices services, EnumerationQuery query, AiTimeseriesView series)
        {
            ValidateScalarFilters(query);
            EnumerationRequest request = services.Enumeration.Prepare(query, Collection("ai-timeseries", "days", "metric", "granularity", "assistantId", "endpointId"), true);
            EnumerationResult<AiTimeseriesPoint> page = CatalogEnumeration.Page(request, series.Points,
                point => point.TimestampUtc.ToString("O", CultureInfo.InvariantCulture), point => point.TimestampUtc,
                labels: point => point.Labels, tags: point => point.Tags);
            return CatalogEnumerationResponse<AiTimeseriesPoint>.From(page, new Dictionary<string, object?> { ["metric"] = series.Metric, ["granularity"] = series.Granularity });
        }

        /// <summary>Page individual meter/bucket observations, retaining scalar usage context.</summary>
        public static CatalogEnumerationResponse<UsageObservationRow> Usage(EnumerationService enumeration, EnumerationQuery query, UsageSummary summary)
        {
            ValidateScalarFilters(query);
            EnumerationRequest request = enumeration.Prepare(query, Collection("usage-summary-observations", "period", "from", "to", "bucket", "meters"), true);
            List<UsageObservationRow> rows = new List<UsageObservationRow>();
            foreach (UsageMeterSeries meter in summary.Meters)
            {
                for (int index = 0; index < summary.Buckets.Count; index++)
                {
                    UsageBucketView bucket = summary.Buckets[index];
                    rows.Add(new UsageObservationRow
                    {
                        Id = meter.Meter + ":" + bucket.StartUtc.ToString("O", CultureInfo.InvariantCulture),
                        Meter = meter.Meter, Group = meter.Group, Kind = meter.Kind, Unit = meter.Unit,
                        Total = meter.Total, PreviousTotal = meter.PreviousTotal, Change = meter.Change, Exact = meter.Exact,
                        StartUtc = bucket.StartUtc, EndUtc = bucket.EndUtc, Label = bucket.Label, Value = meter.Values.ElementAtOrDefault(index)
                    });
                }
            }
            EnumerationResult<UsageObservationRow> page = CatalogEnumeration.Page(request, rows, row => row.Id, row => row.StartUtc,
                row => row.Meter + " " + row.Group, labels: row => row.Labels, tags: row => row.Tags);
            return CatalogEnumerationResponse<UsageObservationRow>.From(page, new Dictionary<string, object?>
            {
                ["propertyId"] = summary.PropertyId,
                ["period"] = summary.Period,
                ["timeZone"] = summary.TimeZone,
                ["weekStartsOn"] = summary.WeekStartsOn,
                ["fromUtc"] = summary.FromUtc,
                ["toUtc"] = summary.ToUtc,
                ["previousFromUtc"] = summary.PreviousFromUtc,
                ["previousToUtc"] = summary.PreviousToUtc,
                ["bucket"] = summary.Bucket,
                ["dataThroughUtc"] = summary.DataThroughUtc,
                ["backfillComplete"] = summary.BackfillComplete,
                ["storageReconciliation"] = summary.StorageReconciliation,
                ["generatedUtc"] = summary.GeneratedUtc
            });
        }
    }
}
