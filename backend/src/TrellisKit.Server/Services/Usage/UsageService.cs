namespace TrellisKit.Server.Services.Usage
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Serialization;

    /// <summary>
    /// Usage metering of one property (docs/USAGE_METERING_CONTRACT.md): the rollup pass (backfill, open window
    /// recompute, closing, storage measurement, pruning), the summary with series and period totals, the usage record
    /// enumeration, and the export. Thread safety: safe to call concurrently; the rollup pass itself is serialized by the
    /// worker's job lease.
    /// </summary>
    public class UsageService
    {
        #region Public-Members

        /// <summary>
        /// Property setting key of the time zone.
        /// </summary>
        public const string TimeZoneKey = "usage.timeZone";

        /// <summary>
        /// Property setting key of the first day of the week.
        /// </summary>
        public const string WeekStartKey = "usage.weekStartsOn";

        /// <summary>
        /// Most daily rows an export reads. 1830 days of every meter.
        /// </summary>
        public const int MaxExportRows = 1830 * 40;

        /// <summary>
        /// Longest Hour export in days. 31.
        /// </summary>
        public const int MaxHourExportDays = 31;

        /// <summary>
        /// Recorded counters of this property on this instance.
        /// </summary>
        public UsageRecorder Recorder { get; } = new UsageRecorder();

        /// <summary>
        /// Property id.
        /// </summary>
        public string PropertyId { get; }

        #endregion

        #region Private-Members

        private const int _HourBatch = 168;
        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly IntegrationSet? _Integrations;
        private readonly MetadataService? _Metadata;
        private readonly EnumerationService? _Enumeration;
        private readonly AppLogger _Logger;
        private UsageZone? _Zone = null;
        private DateTime _ZoneLoadedUtc = DateTime.MinValue;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Driver bound to the property.</param>
        /// <param name="settings">Running settings.</param>
        /// <param name="integrations">Integrations (object store for storage measurement), or null to skip measurement.</param>
        /// <param name="metadata">Metadata service (labels and tags of usage records), or null.</param>
        /// <param name="enumeration">Enumeration service, or null (enumeration then unavailable).</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">The driver, settings, or logger is null.</exception>
        /// <exception cref="ArgumentException">The driver is not bound to a property.</exception>
        public UsageService(DatabaseDriverBase database, AppSettings settings, IntegrationSet? integrations, MetadataService? metadata, EnumerationService? enumeration, AppLogger logger)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            PropertyId = database.BoundPropertyId ?? throw new ArgumentException("The usage service needs a property-bound driver.", nameof(database));
            _Integrations = integrations;
            _Metadata = metadata;
            _Enumeration = enumeration;
            Recorder.Enabled = settings.Usage.RecordRequests;
        }

        #endregion

        #region Public-Methods

        /// <summary>Standard enumeration of meter/bucket observations, with scalar summary context.</summary>
        public async Task<CatalogEnumerationResponse<UsageObservationRow>> SummaryPageAsync(EnumerationQuery query,DateTime now,CancellationToken token=default)
        {
            TrellisKit.Server.Services.Metrics.AggregateEnumeration.ValidateScalarFilters(query);
            EnumerationService enumeration=_Enumeration??new EnumerationService(_Settings){PropertyId=PropertyId};
            enumeration.Prepare(query,TrellisKit.Server.Services.Metrics.AggregateEnumeration.Collection("usage-summary-observations","period","from","to","bucket","meters"),true);
            UsageQuery selection=new UsageQuery{Period=query.FilterString("period"),From=query.FilterString("from"),To=query.FilterString("to"),Bucket=query.FilterString("bucket"),Meters=query.FilterString("meters")};
            UsageSummary summary=await SummaryAsync(selection,now,token).ConfigureAwait(false);
            return TrellisKit.Server.Services.Metrics.AggregateEnumeration.Usage(enumeration,query,summary);
        }


        /// <summary>
        /// The property's usage calendar (usage.timeZone and usage.weekStartsOn property settings), cached 30 seconds.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Calendar.</returns>
        public async Task<UsageZone> ZoneAsync(CancellationToken token = default)
        {
            UsageZone? cached = _Zone;
            if (cached != null && (DateTime.UtcNow - _ZoneLoadedUtc).TotalSeconds < 30) return cached;
            List<PropertySettingRow> rows = await _Database.PropertySettings.ReadAllAsync(token).ConfigureAwait(false);
            string? zone = rows.FirstOrDefault(r => r.Key == TimeZoneKey)?.ValueText;
            string? week = rows.FirstOrDefault(r => r.Key == WeekStartKey)?.ValueText;
            UsageZone resolved = UsageZone.From(zone, week);
            _Zone = resolved;
            _ZoneLoadedUtc = DateTime.UtcNow;
            return resolved;
        }

        /// <summary>
        /// Drop the cached calendar (after a property settings change).
        /// </summary>
        public void InvalidateZone()
        {
            _Zone = null;
        }

        /// <summary>
        /// Flush this instance's recorded counters.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Cells written.</returns>
        public Task<int> FlushAsync(CancellationToken token = default)
        {
            return Recorder.FlushAsync(_Database, token);
        }

        /// <summary>
        /// One rollup pass (section 4.2 steps 3 to 7): backfill, open window recompute and closing, storage measurement,
        /// and pruning. The caller holds the job lease and flushes first.
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows written.</returns>
        public async Task<int> RunPassAsync(DateTime nowUtc, CancellationToken token = default)
        {
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                int rows = await PassAsync(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), token).ConfigureAwait(false);
                UsageTelemetry.Add(UsageTelemetry.Passes, 1, "outcome", "ok");
                return rows;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                UsageTelemetry.Add(UsageTelemetry.Passes, 1, "outcome", "error");
                throw;
            }
            finally
            {
                TrellisKitTelemetry.Record(UsageTelemetry.PassDuration, watch.Elapsed.TotalSeconds);
            }
        }

        /// <summary>
        /// GET /api/v1/admin/usage/summary.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<UsageSummary> SummaryAsync(UsageQuery query, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            DateTime now = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
            UsageZone zone = await ZoneAsync(token).ConfigureAwait(false);
            UsageWindow window = ResolveWindow(query, now, zone, out List<UsageMeter> meters);
            Dictionary<string, UsageTotal> totals = await TotalsAsync(window.FromUtc, window.ToUtc, meters, zone, now, token).ConfigureAwait(false);
            Dictionary<string, UsageTotal> previous = await TotalsAsync(window.PreviousFromUtc, window.PreviousToUtc, meters, zone, now, token).ConfigureAwait(false);
            Dictionary<string, List<long?>> series = await SeriesAsync(window, meters, now, token).ConfigureAwait(false);
            UsageState? state = await _Database.Usage.ReadStateAsync(token).ConfigureAwait(false);
            UsageSummary summary = new UsageSummary
            {
                PropertyId = PropertyId,
                Period = window.Period,
                TimeZone = zone.Id,
                WeekStartsOn = zone.WeekStart.ToString(),
                FromUtc = window.FromUtc,
                ToUtc = window.ToUtc,
                PreviousFromUtc = window.PreviousFromUtc,
                PreviousToUtc = window.PreviousToUtc,
                Bucket = window.Bucket,
                Buckets = window.Buckets.Select(b => new UsageBucketView { StartUtc = b.StartUtc, EndUtc = b.EndUtc, Label = b.Label }).ToList(),
                DataThroughUtc = state?.LastRollupUtc,
                BackfillComplete = state?.BackfilledUtc != null,
                StorageReconciliation = new UsageReconciliationView
                {
                    CheckedUtc = state?.StorageScanUtc,
                    Checked = state?.StorageMeasured ?? 0,
                    Missing = state?.StorageMissing ?? 0,
                    DriftBytes = state?.StorageDriftBytes ?? 0
                },
                GeneratedUtc = now
            };
            foreach (UsageMeter m in meters)
            {
                UsageTotal total = totals[m.Id];
                UsageTotal prior = previous[m.Id];
                summary.Meters.Add(new UsageMeterSeries
                {
                    Meter = m.Id,
                    Group = m.Group,
                    Kind = m.Kind,
                    Unit = m.Unit,
                    Total = total.Value,
                    PreviousTotal = prior.Value,
                    Change = prior.Value == 0 ? null : Math.Round((double)(total.Value - prior.Value) / prior.Value, 4),
                    Exact = total.Exact,
                    Values = series[m.Id]
                });
            }
            return summary;
        }

        /// <summary>
        /// Period totals of meters (section 3.4).
        /// </summary>
        /// <param name="fromUtc">Start.</param>
        /// <param name="toUtc">End.</param>
        /// <param name="meters">Meters.</param>
        /// <param name="zone">Calendar.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Total per meter id.</returns>
        public async Task<Dictionary<string, UsageTotal>> TotalsAsync(DateTime fromUtc, DateTime toUtc, IReadOnlyList<UsageMeter> meters, UsageZone zone, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(meters);
            ArgumentNullException.ThrowIfNull(zone);
            Dictionary<string, UsageTotal> result = meters.ToDictionary(m => m.Id, m => new UsageTotal(), StringComparer.Ordinal);
            if (toUtc <= fromUtc || meters.Count == 0) return result;
            DateTime hourlyCutoff = UsagePeriodCalculator.FloorHour(nowUtc).AddDays(-_Settings.Usage.HourlyRetentionDays);

            DateOnly firstDay = zone.LocalDate(fromUtc);
            DateOnly lastDay = zone.LocalDate(toUtc.AddTicks(-1));
            DateOnly fullStart = zone.Midnight(firstDay) == fromUtc ? firstDay : firstDay.AddDays(1);
            DateOnly fullEnd = zone.Midnight(lastDay.AddDays(1)) == toUtc ? lastDay.AddDays(1) : lastDay;
            List<UsageBucketRange> hourly = new List<UsageBucketRange>();
            DateTime dailyStart;
            DateTime dailyEnd;
            if (fullStart >= fullEnd)
            {
                // Inside one local day (or two partial days): hours when available, else the whole days.
                if (fromUtc >= hourlyCutoff)
                {
                    hourly.Add(new UsageBucketRange { Key = "h1", StartUtc = fromUtc, EndUtc = toUtc });
                    dailyStart = dailyEnd = fromUtc;
                }
                else
                {
                    dailyStart = zone.Midnight(firstDay);
                    dailyEnd = zone.Midnight(lastDay.AddDays(1));
                }
            }
            else
            {
                dailyStart = zone.Midnight(fullStart);
                dailyEnd = zone.Midnight(fullEnd);
                if (fromUtc < dailyStart)
                {
                    if (fromUtc >= hourlyCutoff) hourly.Add(new UsageBucketRange { Key = "h1", StartUtc = fromUtc, EndUtc = dailyStart });
                    else dailyStart = zone.Midnight(firstDay);
                }
                if (toUtc > dailyEnd)
                {
                    if (dailyEnd >= hourlyCutoff) hourly.Add(new UsageBucketRange { Key = "h2", StartUtc = dailyEnd, EndUtc = toUtc });
                    else dailyEnd = zone.Midnight(lastDay.AddDays(1));
                }
            }
            DateTime touchStart = zone.Midnight(firstDay);
            DateTime touchEnd = zone.Midnight(lastDay.AddDays(1));
            List<UsageBucketRange> daily = new List<UsageBucketRange> { new UsageBucketRange { Key = "touch", StartUtc = touchStart, EndUtc = touchEnd } };
            if (dailyEnd > dailyStart) daily.Add(new UsageBucketRange { Key = "d", StartUtc = dailyStart, EndUtc = dailyEnd });
            hourly.Add(new UsageBucketRange { Key = "all", StartUtc = Max(fromUtc, hourlyCutoff), EndUtc = toUtc });

            List<string> ids = meters.Select(m => m.Id).Concat(new[] { "users.activeMonthly" }).Distinct(StringComparer.Ordinal).ToList();
            List<UsageAggregate> dailyRows = await _Database.Usage.AggregateDailyAsync(daily, ids, token).ConfigureAwait(false);
            List<UsageAggregate> hourlyRows = hourly[hourly.Count - 1].EndUtc > hourly[hourly.Count - 1].StartUtc || hourly.Count > 1
                ? await _Database.Usage.AggregateHourlyAsync(hourly.Where(h => h.EndUtc > h.StartUtc).ToList(), ids, token).ConfigureAwait(false)
                : new List<UsageAggregate>();

            foreach (UsageMeter m in meters)
            {
                UsageTotal total = result[m.Id];
                if (m.Aggregate == UsageAggregateEnum.Sum)
                {
                    total.Value = Find(dailyRows, "d", m.Id)?.Sum ?? 0;
                    total.Value += Find(hourlyRows, "h1", m.Id)?.Sum ?? 0;
                    total.Value += Find(hourlyRows, "h2", m.Id)?.Sum ?? 0;
                }
                else if (m.Aggregate == UsageAggregateEnum.Last)
                {
                    UsageAggregate? latestHour = m.DailyOnly ? null : Find(hourlyRows, "all", m.Id);
                    total.Value = latestHour != null && latestHour.Rows > 0 ? latestHour.Last : Find(dailyRows, "touch", m.Id)?.Last ?? 0;
                }
                else
                {
                    if (fromUtc >= nowUtc.AddDays(-_Settings.Usage.ActivityRetentionDays))
                    {
                        Dictionary<string, long> live = await _Database.Usage.ComputeAsync(m.Id, new List<UsageBucketRange> { new UsageBucketRange { Key = "p", StartUtc = fromUtc, EndUtc = toUtc } }, false, token).ConfigureAwait(false);
                        total.Value = live["p"];
                    }
                    else
                    {
                        total.Value = Math.Max(Find(dailyRows, "touch", "users.activeMonthly")?.Max ?? 0, Find(dailyRows, "touch", m.Id)?.Max ?? 0);
                        total.Exact = false;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// POST /api/v1/admin/usage/enumerate.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of daily usage records with labels and tags.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken.</exception>
        /// <exception cref="InvalidOperationException">The service was built without an enumeration service.</exception>
        public async Task<EnumerationResult<UsageRecord>> EnumerateAsync(EnumerationQuery? query, CancellationToken token = default)
        {
            if (_Enumeration == null) throw new InvalidOperationException("Usage enumeration needs an enumeration service.");
            EnumerationRequest request = _Enumeration.Prepare(query, UsageCollections.Records, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            UsageRecordFilter filter = new UsageRecordFilter();
            string? meter = request.Query.FilterString("meter");
            if (meter != null)
            {
                List<UsageMeter> resolved = UsageMeters.Resolve(meter, out string? unknown);
                if (unknown != null) errors["filters.meter"] = "Unknown meter or group " + unknown + ".";
                filter.Meters = resolved.Select(m => m.Id).ToList();
            }
            filter.FromDay = Day(request.Query.FilterString("fromDay"), "filters.fromDay", errors);
            filter.ToDay = Day(request.Query.FilterString("toDay"), "filters.toDay", errors);
            filter.Closed = request.Query.FilterBool("closed", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            EnumerationResult<UsageRecord> page = await _Database.Usage.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            if (_Metadata != null && page.Objects.Count > 0)
            {
                Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.UsageRecord, page.Objects.Select(o => o.Id), true, token).ConfigureAwait(false);
                foreach (UsageRecord record in page.Objects)
                {
                    if (!metadata.TryGetValue(record.Id, out ObjectMetadata? meta)) continue;
                    record.Labels = meta.Labels;
                    record.Tags = meta.Tags;
                }
            }
            return page;
        }

        /// <summary>
        /// GET /api/v1/admin/usage/export: daily (or hourly) rows and period totals of a period as JSON or CSV.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="propertyName">Property name for the JSON header.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>File.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<UsageExportFile> ExportAsync(UsageQuery query, DateTime nowUtc, string propertyName, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            DateTime now = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string format = String.IsNullOrWhiteSpace(query.Format) ? "json" : query.Format.Trim().ToLowerInvariant();
            if (format != "json" && format != "csv") errors["format"] = "Must be json or csv.";
            bool hourly = !String.IsNullOrWhiteSpace(query.Granularity) && String.Equals(query.Granularity.Trim(), "Hour", StringComparison.OrdinalIgnoreCase);
            if (!String.IsNullOrWhiteSpace(query.Granularity) && !hourly && !String.Equals(query.Granularity.Trim(), "Day", StringComparison.OrdinalIgnoreCase)) errors["granularity"] = "Must be Day or Hour.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "The export query is invalid.");
            UsageZone zone = await ZoneAsync(token).ConfigureAwait(false);
            UsageQuery bucketless = new UsageQuery { Period = query.Period, From = query.From, To = query.To, Bucket = "Month", Meters = query.Meters };
            UsageWindow window = ResolveWindow(bucketless, now, zone, out List<UsageMeter> meters);
            if (hourly)
            {
                if ((window.ToUtc - window.FromUtc).TotalDays > MaxHourExportDays + 0.0001) throw ApiErrorException.Field("granularity", "Hour exports cover at most " + MaxHourExportDays + " days.");
                if (window.FromUtc < UsagePeriodCalculator.FloorHour(now).AddDays(-_Settings.Usage.HourlyRetentionDays)) throw ApiErrorException.Field("granularity", "Hourly rows are kept for " + _Settings.Usage.HourlyRetentionDays + " days.");
            }
            HashSet<string> wanted = new HashSet<string>(meters.Select(m => m.Id), StringComparer.Ordinal);
            string fromDay = zone.LocalDate(window.FromUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string toDay = zone.LocalDate(window.ToUtc.AddTicks(-1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Dictionary<string, UsageTotal> totals = await TotalsAsync(window.FromUtc, window.ToUtc, meters, zone, now, token).ConfigureAwait(false);
            List<UsageRecord> days = (await _Database.Usage.ReadDailyAsync(fromDay, toDay, MaxExportRows, token).ConfigureAwait(false)).Where(r => wanted.Contains(r.Meter)).ToList();
            List<UsageHourlyValue> hours = hourly
                ? (await _Database.Usage.ReadHourlyAsync(UsagePeriodCalculator.FloorHour(window.FromUtc), window.ToUtc, MaxHourExportDays * 24 * 40, token).ConfigureAwait(false)).Where(h => wanted.Contains(h.Meter)).ToList()
                : new List<UsageHourlyValue>();
            bool complete = days.Count > 0 && days.All(d => d.Closed);
            string? oldestOpen = await _Database.Usage.OldestOpenDayAsync(token).ConfigureAwait(false);
            string? latest = await _Database.Usage.LatestDayAsync(token).ConfigureAwait(false);
            string? closedThrough = oldestOpen != null
                ? DateOnly.ParseExact(oldestOpen, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : latest;
            string fileBase = "usage-" + PropertyId + "-" + fromDay + "-" + toDay;
            UsageTelemetry.Add(UsageTelemetry.Exports, 1, "format", format);
            if (format == "csv")
            {
                CsvWriter csv = new CsvWriter();
                csv.Row("propertyId", "meter", "kind", "unit", "granularity", "day", "periodStartUtc", "periodEndUtc", "value", "closed", "timeZone");
                if (hourly)
                {
                    foreach (UsageHourlyValue h in hours)
                    {
                        UsageMeter? m = UsageMeters.Find(h.Meter);
                        csv.Row(PropertyId, h.Meter, m?.Kind.ToString(), m?.Unit, "Hour", zone.LocalDate(h.PeriodStartUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), h.PeriodStartUtc, h.PeriodStartUtc.AddHours(1), h.Value, h.PeriodStartUtc.AddHours(1).AddHours(_Settings.Usage.CloseAfterHours) <= now, zone.Id);
                    }
                }
                else
                {
                    foreach (UsageRecord d in days) csv.Row(PropertyId, d.Meter, d.Kind.ToString(), d.Unit, "Day", d.Day, d.PeriodStartUtc, d.PeriodEndUtc, d.Value, d.Closed, d.TimeZone);
                }
                return new UsageExportFile { FileName = fileBase + ".csv", ContentType = "text/csv; charset=utf-8", Body = csv.ToString() };
            }
            Dictionary<string, object?> document = new Dictionary<string, object?>
            {
                { "schemaVersion", 1 },
                { "propertyId", PropertyId },
                { "propertyName", propertyName },
                { "period", window.Period.ToString() },
                { "fromUtc", window.FromUtc },
                { "toUtc", window.ToUtc },
                { "fromDay", fromDay },
                { "toDay", toDay },
                { "timeZone", zone.Id },
                { "granularity", hourly ? "Hour" : "Day" },
                { "generatedUtc", now },
                { "complete", complete },
                { "closedThroughDay", closedThrough },
                { "totals", meters.Select(m => new Dictionary<string, object?> { { "meter", m.Id }, { "kind", m.Kind.ToString() }, { "unit", m.Unit }, { "value", totals[m.Id].Value }, { "exact", totals[m.Id].Exact } }).ToList() },
                { "rows", hourly
                    ? hours.Select(h => (object)new Dictionary<string, object?> { { "meter", h.Meter }, { "periodStartUtc", h.PeriodStartUtc }, { "periodEndUtc", h.PeriodStartUtc.AddHours(1) }, { "value", h.Value } }).ToList()
                    : days.Select(d => (object)new Dictionary<string, object?> { { "meter", d.Meter }, { "day", d.Day }, { "periodStartUtc", d.PeriodStartUtc }, { "periodEndUtc", d.PeriodEndUtc }, { "value", d.Value }, { "closed", d.Closed }, { "timeZone", d.TimeZone } }).ToList() }
            };
            return new UsageExportFile { FileName = fileBase + ".json", ContentType = "application/json", Body = ApiJson.Serialize(document) };
        }

        /// <summary>
        /// Resolve a query into a window and the meters it asks for.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="zone">Calendar.</param>
        /// <param name="meters">Meters.</param>
        /// <returns>Window.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public UsageWindow ResolveWindow(UsageQuery query, DateTime nowUtc, UsageZone zone, out List<UsageMeter> meters)
        {
            ArgumentNullException.ThrowIfNull(query);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            UsagePeriodEnum period = UsagePeriodCalculator.ParsePeriod(query.Period, errors);
            UsageBucketEnum? bucket = UsagePeriodCalculator.ParseBucket(query.Bucket, errors);
            meters = UsageMeters.Resolve(query.Meters, out string? unknown);
            if (unknown != null) errors["meters"] = "Unknown meter or group " + unknown + ".";
            if (period != UsagePeriodEnum.Range && (query.From != null || query.To != null)) errors["period"] = "from and to need period Range.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "The usage query is invalid.");
            return UsagePeriodCalculator.Resolve(period, query.From, query.To, bucket, nowUtc, zone, _Settings.Usage.HourlyRetentionDays);
        }

        #endregion

        #region Private-Methods

        private async Task<int> PassAsync(DateTime now, CancellationToken token)
        {
            UsageSettings s = _Settings.Usage;
            UsageZone zone = await ZoneAsync(token).ConfigureAwait(false);
            UsageState state = await _Database.Usage.ReadStateAsync(token).ConfigureAwait(false) ?? new UsageState { TimeZone = zone.Id };
            if (state.TimeZone != zone.Id)
            {
                int dropped = await _Database.Usage.DeleteOpenDailyAsync(token).ConfigureAwait(false);
                _Logger.Info("[UsageService] [p:" + PropertyId + "] time zone changed from " + state.TimeZone + " to " + zone.Id + "; recomputing " + dropped + " open daily rows");
                state.TimeZone = zone.Id;
            }
            DateOnly today = zone.LocalDate(now);
            DateTime currentHour = UsagePeriodCalculator.FloorHour(now);
            DateTime windowHour = UsagePeriodCalculator.FloorHour(now.AddHours(-s.CloseAfterHours));
            if (state.BackfillStartUtc == null)
            {
                DateTime earliest = await _Database.Usage.EarliestSourceUtcAsync(token).ConfigureAwait(false) ?? now;
                if (earliest > now) earliest = now;
                state.BackfillStartUtc = zone.Midnight(zone.LocalDate(earliest));
                state.BackfillCursorUtc = state.BackfillStartUtc;
                DateTime retention = currentHour.AddDays(-s.HourlyRetentionDays);
                state.HourlyCursorUtc = Max(UsagePeriodCalculator.FloorHour(state.BackfillStartUtc.Value), retention);
            }
            // Storage measurement first, so the gauges computed below include this pass's measurements.
            int rows = await MeasureStorageAsync(state, now, token).ConfigureAwait(false);

            // Daily backfill, oldest first, at most BackfillDaysPerPass local days.
            if (state.BackfilledUtc == null)
            {
                DateOnly cursor = zone.LocalDate(state.BackfillCursorUtc ?? state.BackfillStartUtc.Value);
                DateOnly end = cursor.AddDays(s.BackfillDaysPerPass - 1);
                if (end > today) end = today;
                if (cursor <= end)
                {
                    rows += await ComputeDaysAsync(cursor, end, zone, now, token).ConfigureAwait(false);
                    UsageTelemetry.Add(UsageTelemetry.BackfillDays, end.DayNumber - cursor.DayNumber + 1);
                }
                DateOnly next = end.AddDays(1);
                state.BackfillCursorUtc = zone.Midnight(next);
                if (next > today) state.BackfilledUtc = now;
            }

            // Hourly backfill up to the open window, at most a week of hours per pass.
            DateTime hourCursor = state.HourlyCursorUtc ?? windowHour;
            if (hourCursor < windowHour)
            {
                DateTime hourEnd = Min(hourCursor.AddHours(_HourBatch), windowHour);
                rows += await ComputeHoursAsync(hourCursor, hourEnd, token).ConfigureAwait(false);
                state.HourlyCursorUtc = hourEnd;
            }

            // Open window: every hour that can still change, and every open day.
            Dictionary<string, long> latest = new Dictionary<string, long>(StringComparer.Ordinal);
            rows += await ComputeHoursAsync(windowHour, currentHour.AddHours(1), token, latest).ConfigureAwait(false);
            DateOnly openDay = zone.LocalDate(now.AddHours(-s.CloseAfterHours)).AddDays(-1);
            string? oldestOpen = await _Database.Usage.OldestOpenDayAsync(token).ConfigureAwait(false);
            if (oldestOpen != null && DateOnly.TryParseExact(oldestOpen, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly open) && open < openDay)
            {
                // Rows left open by downtime: close them, oldest first, within the per-pass bound.
                DateOnly stale = open.AddDays(Math.Min(s.BackfillDaysPerPass, openDay.DayNumber - open.DayNumber) - 1);
                rows += await ComputeDaysAsync(open, stale, zone, now, token).ConfigureAwait(false);
            }
            rows += await ComputeDaysAsync(openDay > today ? today : openDay, today, zone, now, token).ConfigureAwait(false);

            DateTime? dailyBefore = s.DailyRetentionDays == 0 ? null : zone.Midnight(today.AddDays(-s.DailyRetentionDays));
            int pruned = await _Database.Usage.PruneAsync(currentHour.AddDays(-s.HourlyRetentionDays), currentHour.AddDays(-s.ActivityRetentionDays), dailyBefore, 5000, token).ConfigureAwait(false);
            if (pruned > 0) _Logger.Debug("[UsageService] [p:" + PropertyId + "] pruned " + pruned + " usage rows");

            state.LastRollupUtc = DateTime.UtcNow;
            await _Database.Usage.WriteStateAsync(state, token).ConfigureAwait(false);
            Dictionary<string, long> storage = UsageMeters.StorageClasses.Concat(new[] { "storage.total" }).ToDictionary(c => c, c => latest.TryGetValue(c, out long v) ? v : 0, StringComparer.Ordinal);
            UsageTelemetry.SetLatest(PropertyId, storage, latest.TryGetValue("users.total", out long users) ? users : 0, state.LastRollupUtc.Value);
            return rows;
        }

        private Task<int> ComputeHoursAsync(DateTime fromHour, DateTime toHour, CancellationToken token)
        {
            return ComputeHoursAsync(fromHour, toHour, token, null);
        }

        private async Task<int> ComputeHoursAsync(DateTime fromHour, DateTime toHour, CancellationToken token, Dictionary<string, long>? latest)
        {
            List<UsageBucketRange> buckets = new List<UsageBucketRange>();
            for (DateTime t = fromHour; t < toHour && buckets.Count < 1000; t = t.AddHours(1))
                buckets.Add(new UsageBucketRange { Key = t.Ticks.ToString(CultureInfo.InvariantCulture), StartUtc = t, EndUtc = t.AddHours(1) });
            if (buckets.Count == 0) return 0;
            List<UsageHourlyValue> values = new List<UsageHourlyValue>();
            foreach (UsageMeter m in UsageMeters.All.Where(m => !m.DailyOnly && m.Source != UsageMeterSourceEnum.Recorded))
            {
                Dictionary<string, long> computed = await _Database.Usage.ComputeAsync(m.Id, buckets, true, token).ConfigureAwait(false);
                foreach (UsageBucketRange b in buckets) values.Add(new UsageHourlyValue { Meter = m.Id, PeriodStartUtc = b.StartUtc, Value = computed[b.Key] });
                if (latest != null) latest[m.Id] = computed[buckets[buckets.Count - 1].Key];
            }
            int written = 0;
            foreach (UsageHourlyValue[] chunk in values.Chunk(5000)) written += await _Database.Usage.WriteHourlyAsync(chunk, token).ConfigureAwait(false);
            UsageTelemetry.Add(UsageTelemetry.Rows, written, "granularity", "hour");
            return written;
        }

        private async Task<int> ComputeDaysAsync(DateOnly first, DateOnly last, UsageZone zone, DateTime now, CancellationToken token)
        {
            if (last < first) return 0;
            List<DateOnly> days = new List<DateOnly>();
            for (DateOnly d = first; d <= last && days.Count < 366; d = d.AddDays(1)) days.Add(d);
            List<UsageBucketRange> buckets = days.Select(d => Bucket(d, d, zone)).ToList();
            List<UsageBucketRange> weekly = days.Select(d => Bucket(d.AddDays(-6), d, zone)).ToList();
            List<UsageBucketRange> monthly = days.Select(d => Bucket(d.AddDays(-29), d, zone)).ToList();
            List<UsageDailyValue> values = new List<UsageDailyValue>();
            foreach (UsageMeter m in UsageMeters.All)
            {
                List<UsageBucketRange> source = m.Id == "users.activeWeekly" ? weekly : m.Id == "users.activeMonthly" ? monthly : buckets;
                Dictionary<string, long> computed = await _Database.Usage.ComputeAsync(m.Id, source, false, token).ConfigureAwait(false);
                for (int i = 0; i < days.Count; i++)
                {
                    DateTime end = buckets[i].EndUtc;
                    values.Add(new UsageDailyValue
                    {
                        Meter = m.Id,
                        Day = buckets[i].Key,
                        TimeZone = zone.Id,
                        PeriodStartUtc = buckets[i].StartUtc,
                        PeriodEndUtc = end,
                        Value = computed[source[i].Key],
                        Closed = end.AddHours(_Settings.Usage.CloseAfterHours) <= now
                    });
                }
            }
            int written = 0;
            foreach (UsageDailyValue[] chunk in values.Chunk(5000)) written += await _Database.Usage.WriteDailyAsync(chunk, token).ConfigureAwait(false);
            UsageTelemetry.Add(UsageTelemetry.Rows, written, "granularity", "day");
            return written;
        }

        private async Task<Dictionary<string, List<long?>>> SeriesAsync(UsageWindow window, IReadOnlyList<UsageMeter> meters, DateTime now, CancellationToken token)
        {
            Dictionary<string, List<long?>> series = new Dictionary<string, List<long?>>(StringComparer.Ordinal);
            List<UsageBucketRange> select = window.Buckets.Select(b => new UsageBucketRange { Key = b.Key, StartUtc = b.SelectStartUtc, EndUtc = b.SelectEndUtc }).ToList();
            bool hourly = window.Bucket == UsageBucketEnum.Hour;
            List<string> ids = meters.Where(m => !hourly || !m.DailyOnly).Select(m => m.Id).ToList();
            List<UsageAggregate> rows = hourly
                ? await _Database.Usage.AggregateHourlyAsync(select, ids, token).ConfigureAwait(false)
                : await _Database.Usage.AggregateDailyAsync(select, ids, token).ConfigureAwait(false);
            Dictionary<string, UsageAggregate> byKey = rows.ToDictionary(r => r.Key + "|" + r.Meter, StringComparer.Ordinal);
            Dictionary<string, long>? live = null;
            bool larger = window.Bucket == UsageBucketEnum.Week || window.Bucket == UsageBucketEnum.Month;
            DateTime activityCutoff = now.AddDays(-_Settings.Usage.ActivityRetentionDays);
            foreach (UsageMeter m in meters)
            {
                List<long?> values = new List<long?>();
                if (hourly && m.DailyOnly)
                {
                    foreach (UsageWindowBucket b in window.Buckets) values.Add(null);
                    series[m.Id] = values;
                    continue;
                }
                if (m.Aggregate == UsageAggregateEnum.Distinct && larger && live == null)
                {
                    List<UsageBucketRange> recent = window.Buckets.Where(b => b.StartUtc >= activityCutoff).Select(b => new UsageBucketRange { Key = b.Key, StartUtc = b.StartUtc, EndUtc = b.EndUtc }).ToList();
                    live = await _Database.Usage.ComputeAsync(m.Id, recent, false, token).ConfigureAwait(false);
                }
                foreach (UsageWindowBucket b in window.Buckets)
                {
                    byKey.TryGetValue(b.Key + "|" + m.Id, out UsageAggregate? a);
                    if (m.Aggregate == UsageAggregateEnum.Sum) values.Add(a?.Sum ?? 0);
                    else if (m.Aggregate == UsageAggregateEnum.Last) values.Add(a?.Last ?? 0);
                    else if (larger && live != null && live.TryGetValue(b.Key, out long distinct)) values.Add(distinct);
                    else values.Add(larger ? a?.Max ?? 0 : a?.Sum ?? 0);
                }
                series[m.Id] = values;
            }
            return series;
        }

        private async Task<int> MeasureStorageAsync(UsageState state, DateTime now, CancellationToken token)
        {
            if (_Integrations == null) return 0;
            IObjectStore store = _Integrations.ObjectStore;
            int batch = _Settings.Usage.StorageBatchSize;
            int measured = 0;
            List<UsageMediaItem> videos = await _Database.Usage.VideosToMeasureAsync(now.AddDays(-_Settings.Usage.StorageReconcileDays), batch, token).ConfigureAwait(false);
            foreach (UsageMediaItem video in videos)
            {
                long bytes = 0;
                long objects = 0;
                foreach (string prefix in new[] { MediaKeys.VideoPrefix(PropertyId, video.MediaId), MediaKeys.PublicVideoPrefix(PropertyId, video.MediaId) })
                {
                    List<string> keys = await store.ListKeysAsync(MediaLocationEnum.Transcoded, prefix, token).ConfigureAwait(false);
                    foreach (string key in keys.Take(2000))
                    {
                        ObjectHead? head = await store.HeadAsync(MediaLocationEnum.Transcoded, key, token).ConfigureAwait(false);
                        if (head == null) continue;
                        bytes += head.SizeBytes;
                        objects++;
                    }
                }
                await _Database.Usage.WriteMediaSizeAsync(video.MediaId, bytes, objects, now, token).ConfigureAwait(false);
                UsageTelemetry.Add(UsageTelemetry.StorageMeasured, 1, "outcome", "ok");
                measured++;
            }

            List<UsageMediaItem> originals = await _Database.Usage.OriginalsAfterAsync(state.StorageCursor, batch, token).ConfigureAwait(false);
            long missing = 0;
            long drift = 0;
            foreach (UsageMediaItem item in originals)
            {
                ObjectHead? head = await store.HeadAsync(item.SourceLocation, item.SourceKey, token).ConfigureAwait(false);
                if (head == null)
                {
                    missing++;
                    UsageTelemetry.Add(UsageTelemetry.StorageMeasured, 1, "outcome", "missing");
                    continue;
                }
                drift += head.SizeBytes - item.SizeBytes;
                UsageTelemetry.Add(UsageTelemetry.StorageMeasured, 1, "outcome", "ok");
            }
            state.StorageCursor = originals.Count < batch ? null : originals[originals.Count - 1].MediaId;
            state.StorageMeasured = originals.Count;
            state.StorageMissing = missing;
            state.StorageDriftBytes = drift;
            state.StorageScanUtc = now;
            return measured;
        }

        private static UsageBucketRange Bucket(DateOnly startDay, DateOnly day, UsageZone zone)
        {
            return new UsageBucketRange { Key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StartUtc = zone.Midnight(startDay), EndUtc = zone.Midnight(day.AddDays(1)) };
        }

        private static UsageAggregate? Find(List<UsageAggregate> rows, string key, string meter)
        {
            return rows.FirstOrDefault(r => r.Key == key && r.Meter == meter);
        }

        private static string? Day(string? text, string field, Dictionary<string, string> errors)
        {
            if (text == null) return null;
            if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly _)) return text;
            errors[field] = "Must be a date (yyyy-MM-dd).";
            return null;
        }

        private static DateTime Max(DateTime a, DateTime b)
        {
            return a > b ? a : b;
        }

        private static DateTime Min(DateTime a, DateTime b)
        {
            return a < b ? a : b;
        }

        #endregion
    }
}
