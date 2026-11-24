namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared usage metering methods (docs/USAGE_METERING_CONTRACT.md sections 2 and 4). Every computation receives an
    /// explicit list of buckets (computed by the caller in the property's time zone) and evaluates each meter with one
    /// statement over an unnest of the bucket bounds, so the database never decides local day boundaries.
    /// </summary>
    internal abstract class UsageMethodsBase : MethodsBase, IUsageMethods
    {
        private const int _MaxBuckets = 1000;
        private const int _MaxWrite = 5000;
        private const string _DailyColumns = "ud.id, ud.meter, ud.day, ud.timezone, ud.periodstartutc, ud.periodendutc, ud.value, ud.closed, ud.computedutc, ud.createdutc";

        protected UsageMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<UsageState?> ReadStateAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM usagestate WHERE " + Scope() + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            return new UsageState
            {
                TimeZone = RowReader.GetString(row, "timezone"),
                BackfillStartUtc = Utc(RowReader.GetNullableDateTime(row, "backfillstartutc")),
                BackfillCursorUtc = Utc(RowReader.GetNullableDateTime(row, "backfillcursorutc")),
                HourlyCursorUtc = Utc(RowReader.GetNullableDateTime(row, "hourlycursorutc")),
                BackfilledUtc = Utc(RowReader.GetNullableDateTime(row, "backfilledutc")),
                LastRollupUtc = Utc(RowReader.GetNullableDateTime(row, "lastrolluputc")),
                LastFlushUtc = Utc(RowReader.GetNullableDateTime(row, "lastflushutc")),
                StorageScanUtc = Utc(RowReader.GetNullableDateTime(row, "storagescanutc")),
                StorageCursor = RowReader.GetNullableString(row, "storagecursor"),
                StorageMeasured = RowReader.GetLong(row, "storagemeasured"),
                StorageDriftBytes = RowReader.GetLong(row, "storagedriftbytes"),
                StorageMissing = RowReader.GetLong(row, "storagemissing")
            };
        }

        public Task WriteStateAsync(UsageState state, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(state);
            string sql = "INSERT INTO usagestate (propertyid, timezone, backfillstartutc, backfillcursorutc, hourlycursorutc, backfilledutc, lastrolluputc, lastflushutc, storagescanutc, storagecursor, storagemeasured, storagedriftbytes, storagemissing, updatedutc) VALUES (" +
                Pid + ", " + D.Str(state.TimeZone) + ", " + D.Ts(state.BackfillStartUtc) + ", " + D.Ts(state.BackfillCursorUtc) + ", " + D.Ts(state.HourlyCursorUtc) + ", " + D.Ts(state.BackfilledUtc) + ", " +
                D.Ts(state.LastRollupUtc) + ", " + D.Ts(state.LastFlushUtc) + ", " + D.Ts(state.StorageScanUtc) + ", " + D.Str(state.StorageCursor) + ", " + D.Num(state.StorageMeasured) + ", " +
                D.Num(state.StorageDriftBytes) + ", " + D.Num(state.StorageMissing) + ", " + D.Ts(DateTime.UtcNow) + ") ON CONFLICT (propertyid) DO UPDATE SET timezone = EXCLUDED.timezone, " +
                "backfillstartutc = EXCLUDED.backfillstartutc, backfillcursorutc = EXCLUDED.backfillcursorutc, hourlycursorutc = EXCLUDED.hourlycursorutc, backfilledutc = EXCLUDED.backfilledutc, " +
                "lastrolluputc = EXCLUDED.lastrolluputc, lastflushutc = COALESCE(EXCLUDED.lastflushutc, usagestate.lastflushutc), storagescanutc = EXCLUDED.storagescanutc, storagecursor = EXCLUDED.storagecursor, " +
                "storagemeasured = EXCLUDED.storagemeasured, storagedriftbytes = EXCLUDED.storagedriftbytes, storagemissing = EXCLUDED.storagemissing, updatedutc = EXCLUDED.updatedutc;";
            return QueryAsync(sql, token);
        }

        public async Task<DateTime?> EarliestSourceUtcAsync(CancellationToken token = default)
        {
            string sql = "SELECT LEAST(" +
                "(SELECT MIN(createdutc) FROM users WHERE " + Scope() + "), " +
                "(SELECT MIN(createdutc) FROM feedposts WHERE " + Scope() + "), " +
                "(SELECT MIN(createdutc) FROM forumthreads WHERE " + Scope() + "), " +
                "(SELECT MIN(createdutc) FROM comments WHERE " + Scope() + "), " +
                "(SELECT MIN(createdutc) FROM mediaattachments WHERE " + Scope() + ")) AS earliest;";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Utc(RowReader.GetNullableDateTime(table.Rows[0], "earliest"));
        }

        public async Task<Dictionary<string, long>> ComputeAsync(string meter, IReadOnlyList<UsageBucketRange> buckets, bool hourly, CancellationToken token = default)
        {
            RequireText(meter, nameof(meter));
            ArgumentNullException.ThrowIfNull(buckets);
            if (buckets.Count > _MaxBuckets) throw new ArgumentException("At most " + _MaxBuckets + " buckets per computation.", nameof(buckets));
            Dictionary<string, long> result = new Dictionary<string, long>(StringComparer.Ordinal);
            if (buckets.Count == 0) return result;
            string expression = Expression(meter, hourly);
            DataTable table = await QueryAsync("SELECT b.k AS k, (" + expression + ") AS v FROM " + Unnest(buckets) + ";", token).ConfigureAwait(false);
            foreach (UsageBucketRange b in buckets) result[b.Key] = 0;
            foreach (DataRow row in table.Rows) result[RowReader.GetString(row, "k")] = RowReader.GetNullableLong(row, "v") ?? 0;
            return result;
        }

        public async Task<int> WriteHourlyAsync(IReadOnlyList<UsageHourlyValue> values, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count == 0) return 0;
            if (values.Count > _MaxWrite) throw new ArgumentException("At most " + _MaxWrite + " values per write.", nameof(values));
            string now = D.Ts(DateTime.UtcNow);
            string rows = String.Join(", ", values.Select(v => "(" + Pid + ", " + D.Str(v.Meter) + ", " + D.Ts(v.PeriodStartUtc) + ", " + D.Num(v.Value) + ", " + now + ")"));
            await QueryAsync("INSERT INTO usagehourly (propertyid, meter, periodstartutc, value, computedutc) VALUES " + rows +
                " ON CONFLICT (propertyid, meter, periodstartutc) DO UPDATE SET value = EXCLUDED.value, computedutc = EXCLUDED.computedutc;", token).ConfigureAwait(false);
            return values.Count;
        }

        public async Task<int> AddHourlyAsync(IReadOnlyList<UsageHourlyValue> values, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count == 0) return 0;
            if (values.Count > _MaxWrite) throw new ArgumentException("At most " + _MaxWrite + " values per write.", nameof(values));
            string now = D.Ts(DateTime.UtcNow);
            string rows = String.Join(", ", values.Select(v => "(" + Pid + ", " + D.Str(v.Meter) + ", " + D.Ts(v.PeriodStartUtc) + ", " + D.Num(v.Value) + ", " + now + ")"));
            await QueryAsync("INSERT INTO usagehourly (propertyid, meter, periodstartutc, value, computedutc) VALUES " + rows +
                " ON CONFLICT (propertyid, meter, periodstartutc) DO UPDATE SET value = usagehourly.value + EXCLUDED.value, computedutc = EXCLUDED.computedutc;", token).ConfigureAwait(false);
            return values.Count;
        }

        public async Task<int> WriteDailyAsync(IReadOnlyList<UsageDailyValue> values, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count == 0) return 0;
            if (values.Count > _MaxWrite) throw new ArgumentException("At most " + _MaxWrite + " values per write.", nameof(values));
            string now = D.Ts(DateTime.UtcNow);
            string rows = String.Join(", ", values.Select(v => "(" + Pid + ", " + D.Str(UsageMeters.RecordId(v.Meter, v.Day)) + ", " + D.Str(v.Meter) + ", " + D.Str(v.Day) + ", " + D.Str(v.TimeZone) + ", " +
                D.Ts(v.PeriodStartUtc) + ", " + D.Ts(v.PeriodEndUtc) + ", " + D.Num(v.Value) + ", " + D.Bool(v.Closed) + ", " + now + ", " + now + ")"));
            DataTable table = await QueryAsync("INSERT INTO usagedaily (propertyid, id, meter, day, timezone, periodstartutc, periodendutc, value, closed, computedutc, createdutc) VALUES " + rows +
                " ON CONFLICT (propertyid, meter, day) DO UPDATE SET value = EXCLUDED.value, closed = EXCLUDED.closed, timezone = EXCLUDED.timezone, periodstartutc = EXCLUDED.periodstartutc, " +
                "periodendutc = EXCLUDED.periodendutc, computedutc = EXCLUDED.computedutc WHERE usagedaily.closed = FALSE RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<string?> OldestOpenDayAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT MIN(day) AS d FROM usagedaily WHERE " + Scope() + " AND closed = FALSE;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : RowReader.GetNullableString(table.Rows[0], "d");
        }

        public async Task<string?> LatestDayAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT MAX(day) AS d FROM usagedaily WHERE " + Scope() + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : RowReader.GetNullableString(table.Rows[0], "d");
        }

        public async Task<int> DeleteOpenDailyAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("DELETE FROM usagedaily WHERE " + Scope() + " AND closed = FALSE RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public Task<List<UsageAggregate>> AggregateDailyAsync(IReadOnlyList<UsageBucketRange> buckets, IReadOnlyList<string> meters, CancellationToken token = default)
        {
            return AggregateAsync("usagedaily", buckets, meters, token);
        }

        public Task<List<UsageAggregate>> AggregateHourlyAsync(IReadOnlyList<UsageBucketRange> buckets, IReadOnlyList<string> meters, CancellationToken token = default)
        {
            return AggregateAsync("usagehourly", buckets, meters, token);
        }

        public Task<EnumerationResult<UsageRecord>> EnumerateAsync(EnumerationRequest request, UsageRecordFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (filter.Meters.Count > 0) conditions.Add("ud.meter IN " + D.InList(filter.Meters));
            if (!String.IsNullOrEmpty(filter.FromDay)) conditions.Add("ud.day >= " + D.Str(filter.FromDay));
            if (!String.IsNullOrEmpty(filter.ToDay)) conditions.Add("ud.day <= " + D.Str(filter.ToDay));
            if (filter.Closed != null) conditions.Add("ud.closed = " + D.Bool(filter.Closed.Value));
            return EnumerateAsync(request, _DailyColumns, "usagedaily ud", conditions, MapRecord, token);
        }

        public async Task<List<UsageRecord>> ReadDailyAsync(string fromDay, string toDay, int maxRows, CancellationToken token = default)
        {
            RequireText(fromDay, nameof(fromDay));
            RequireText(toDay, nameof(toDay));
            int limit = Math.Clamp(maxRows, 1, 100000);
            DataTable table = await QueryAsync("SELECT " + _DailyColumns + " FROM usagedaily ud WHERE " + Scope("ud") + " AND ud.day >= " + D.Str(fromDay) + " AND ud.day <= " + D.Str(toDay) +
                " ORDER BY ud.day, ud.meter LIMIT " + D.Num(limit) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapRecord).ToList();
        }

        public async Task<List<UsageHourlyValue>> ReadHourlyAsync(DateTime fromUtc, DateTime toUtc, int maxRows, CancellationToken token = default)
        {
            int limit = Math.Clamp(maxRows, 1, 100000);
            DataTable table = await QueryAsync("SELECT meter, periodstartutc, value FROM usagehourly WHERE " + Scope() + " AND periodstartutc >= " + D.Ts(fromUtc) + " AND periodstartutc < " + D.Ts(toUtc) +
                " ORDER BY periodstartutc, meter LIMIT " + D.Num(limit) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new UsageHourlyValue
            {
                Meter = RowReader.GetString(row, "meter"),
                PeriodStartUtc = DateTime.SpecifyKind(RowReader.GetDateTime(row, "periodstartutc"), DateTimeKind.Utc),
                Value = RowReader.GetLong(row, "value")
            }).ToList();
        }

        public Task RecordActivityAsync(string userId, DateTime activeUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DateTime u = activeUtc.ToUniversalTime();
            DateTime hour = new DateTime(u.Year, u.Month, u.Day, u.Hour, 0, 0, DateTimeKind.Utc);
            return QueryAsync("INSERT INTO usageactivity (propertyid, userid, hourutc) VALUES (" + Pid + ", " + D.Str(userId) + ", " + D.Ts(hour) + ") ON CONFLICT DO NOTHING;", token);
        }

        public async Task<List<UsageMediaItem>> VideosToMeasureAsync(DateTime staleBeforeUtc, int maxResults, CancellationToken token = default)
        {
            int limit = Math.Clamp(maxResults, 1, 1000);
            string live = Scope("m") + " AND m.kind = " + D.Enum(MediaKindEnum.Video) + " AND m.status = " + D.Enum(MediaStatusEnum.Ready) + " AND m.deletedutc IS NULL";
            string sql = "SELECT * FROM (" +
                "(SELECT m.id, m.sizebytes, m.sourcekey, m.kind, 0 AS pass, m.createdutc AS sortkey FROM mediaattachments m WHERE " + live +
                " AND NOT EXISTS (SELECT 1 FROM usagemediasizes z WHERE " + Scope("z") + " AND z.mediaid = m.id) ORDER BY m.createdutc DESC LIMIT " + D.Num(limit) + ") UNION ALL " +
                "(SELECT m.id, m.sizebytes, m.sourcekey, m.kind, 1 AS pass, z.measuredutc AS sortkey FROM mediaattachments m JOIN usagemediasizes z ON " + Scope("z") + " AND z.mediaid = m.id WHERE " + live +
                " AND z.measuredutc < " + D.Ts(staleBeforeUtc) + " ORDER BY z.measuredutc LIMIT " + D.Num(limit) + ")) x ORDER BY x.pass, CASE WHEN x.pass = 0 THEN -EXTRACT(EPOCH FROM x.sortkey) ELSE EXTRACT(EPOCH FROM x.sortkey) END LIMIT " + D.Num(limit) + ";";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapMedia).ToList();
        }

        public Task WriteMediaSizeAsync(string mediaId, long sizeBytes, long objectCount, DateTime measuredUtc, CancellationToken token = default)
        {
            RequireText(mediaId, nameof(mediaId));
            return QueryAsync("INSERT INTO usagemediasizes (propertyid, mediaid, sizebytes, objectcount, measuredutc) VALUES (" + Pid + ", " + D.Str(mediaId) + ", " + D.Num(Math.Max(0, sizeBytes)) + ", " +
                D.Num(Math.Max(0, objectCount)) + ", " + D.Ts(measuredUtc) + ") ON CONFLICT (propertyid, mediaid) DO UPDATE SET sizebytes = EXCLUDED.sizebytes, objectcount = EXCLUDED.objectcount, measuredutc = EXCLUDED.measuredutc;", token);
        }

        public async Task<List<UsageMediaItem>> OriginalsAfterAsync(string? afterId, int maxResults, CancellationToken token = default)
        {
            int limit = Math.Clamp(maxResults, 1, 1000);
            string after = String.IsNullOrEmpty(afterId) ? String.Empty : " AND m.id > " + D.Str(afterId);
            DataTable table = await QueryAsync("SELECT m.id, m.sizebytes, m.sourcekey, m.kind FROM mediaattachments m WHERE " + Scope("m") + " AND m.deletedutc IS NULL AND m.status <> " +
                D.Enum(MediaStatusEnum.Uploading) + " AND m.sourcekey <> ''" + after + " ORDER BY m.id LIMIT " + D.Num(limit) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapMedia).ToList();
        }

        public async Task<int> PruneAsync(DateTime hourlyBeforeUtc, DateTime activityBeforeUtc, DateTime? dailyBeforeUtc, int maxRows, CancellationToken token = default)
        {
            string limit = D.Num(Math.Clamp(maxRows, 1, 5000));
            int deleted = 0;
            deleted += (await QueryAsync("DELETE FROM usagehourly WHERE " + Scope() + " AND ctid IN (SELECT ctid FROM usagehourly WHERE " + Scope() + " AND periodstartutc < " + D.Ts(hourlyBeforeUtc) +
                " LIMIT " + limit + ") RETURNING meter;", token).ConfigureAwait(false)).Rows.Count;
            deleted += (await QueryAsync("DELETE FROM usageactivity WHERE " + Scope() + " AND ctid IN (SELECT ctid FROM usageactivity WHERE " + Scope() + " AND hourutc < " + D.Ts(activityBeforeUtc) +
                " LIMIT " + limit + ") RETURNING userid;", token).ConfigureAwait(false)).Rows.Count;
            deleted += (await QueryAsync("DELETE FROM usagemediasizes WHERE " + Scope() + " AND mediaid IN (SELECT z.mediaid FROM usagemediasizes z WHERE " + Scope("z") +
                " AND NOT EXISTS (SELECT 1 FROM mediaattachments m WHERE " + Scope("m") + " AND m.id = z.mediaid AND m.deletedutc IS NULL) LIMIT " + limit + ") RETURNING mediaid;", token).ConfigureAwait(false)).Rows.Count;
            if (dailyBeforeUtc != null)
            {
                deleted += (await QueryAsync("DELETE FROM usagedaily WHERE " + Scope() + " AND ctid IN (SELECT ctid FROM usagedaily WHERE " + Scope() + " AND periodstartutc < " + D.Ts(dailyBeforeUtc.Value) +
                    " LIMIT " + limit + ") RETURNING id;", token).ConfigureAwait(false)).Rows.Count;
            }
            return deleted;
        }

        private async Task<List<UsageAggregate>> AggregateAsync(string table, IReadOnlyList<UsageBucketRange> buckets, IReadOnlyList<string> meters, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(buckets);
            ArgumentNullException.ThrowIfNull(meters);
            if (buckets.Count > _MaxBuckets) throw new ArgumentException("At most " + _MaxBuckets + " buckets per aggregate.", nameof(buckets));
            List<UsageAggregate> result = new List<UsageAggregate>();
            if (buckets.Count == 0 || meters.Count == 0) return result;
            string sql = "SELECT b.k AS k, r.meter AS meter, COALESCE(SUM(r.value), 0) AS s, COALESCE(MAX(r.value), 0) AS mx, COUNT(*) AS n, " +
                "(array_agg(r.value ORDER BY r.periodstartutc DESC))[1] AS l FROM " + Unnest(buckets) + " JOIN " + table + " r ON " + Scope("r") +
                " AND r.periodstartutc >= b.s AND r.periodstartutc < b.e AND r.meter IN " + D.InList(meters) + " GROUP BY b.k, r.meter;";
            DataTable rows = await QueryAsync(sql, token).ConfigureAwait(false);
            foreach (DataRow row in rows.Rows)
            {
                result.Add(new UsageAggregate
                {
                    Key = RowReader.GetString(row, "k"),
                    Meter = RowReader.GetString(row, "meter"),
                    Sum = RowReader.GetLong(row, "s"),
                    Max = RowReader.GetLong(row, "mx"),
                    Last = RowReader.GetNullableLong(row, "l") ?? 0,
                    Rows = RowReader.GetLong(row, "n")
                });
            }
            return result;
        }

        private string Unnest(IReadOnlyList<UsageBucketRange> buckets)
        {
            StringBuilder keys = new StringBuilder();
            StringBuilder starts = new StringBuilder();
            StringBuilder ends = new StringBuilder();
            for (int i = 0; i < buckets.Count; i++)
            {
                if (i > 0)
                {
                    keys.Append(", ");
                    starts.Append(", ");
                    ends.Append(", ");
                }
                keys.Append(D.Str(buckets[i].Key));
                starts.Append(D.Ts(buckets[i].StartUtc));
                ends.Append(D.Ts(buckets[i].EndUtc));
            }
            return "unnest(ARRAY[" + keys + "]::text[], ARRAY[" + starts + "]::timestamptz[], ARRAY[" + ends + "]::timestamptz[]) AS b(k, s, e)";
        }

        private string Created(string table, string alias, string column, string extra)
        {
            return "(SELECT COUNT(*) FROM " + table + " " + alias + " WHERE " + Scope(alias) + " AND " + alias + "." + column + " >= b.s AND " + alias + "." + column + " < b.e" + extra + ")";
        }

        private string Live(string table, string alias, string extra)
        {
            return "(SELECT COUNT(*) FROM " + table + " " + alias + " WHERE " + Scope(alias) + " AND " + alias + ".createdutc < b.e AND (" + alias + ".deletedutc IS NULL OR " + alias + ".deletedutc >= b.e)" + extra + ")";
        }

        private string MediaFlow(MediaKindEnum kind, bool bytes)
        {
            return "(SELECT " + (bytes ? "COALESCE(SUM(m.sizebytes), 0)" : "COUNT(*)") + " FROM mediaattachments m WHERE " + Scope("m") + " AND m.kind = " + D.Enum(kind) + " AND m.status <> " +
                D.Enum(MediaStatusEnum.Uploading) + " AND m.createdutc >= b.s AND m.createdutc < b.e)";
        }

        private string MediaLiveCondition(string alias)
        {
            return alias + ".status <> " + D.Enum(MediaStatusEnum.Uploading) + " AND " + alias + ".createdutc < b.e AND (" + alias + ".deletedutc IS NULL OR " + alias + ".deletedutc >= b.e)";
        }

        private string Storage(string meter)
        {
            switch (meter)
            {
                case "storage.originals":
                    return "(SELECT COALESCE(SUM(m.sizebytes), 0) FROM mediaattachments m WHERE " + Scope("m") + " AND " + MediaLiveCondition("m") + ")";
                case "storage.imageVariants":
                    return "(SELECT COALESCE(SUM(v.sizebytes * CASE WHEN m.ispublic THEN 2 ELSE 1 END), 0) FROM mediavariants v JOIN mediaattachments m ON " + Scope("m") + " AND m.id = v.mediaid WHERE " +
                        Scope("v") + " AND " + MediaLiveCondition("m") + ")";
                case "storage.videoRenditions":
                    return "(SELECT COALESCE(SUM(z.sizebytes), 0) FROM usagemediasizes z JOIN mediaattachments m ON " + Scope("m") + " AND m.id = z.mediaid WHERE " + Scope("z") +
                        " AND COALESCE(m.readyutc, m.createdutc) < b.e AND (m.deletedutc IS NULL OR m.deletedutc >= b.e))";
                case "storage.updateAssets":
                    return "(SELECT COALESCE(SUM(ua.sizebytes), 0) FROM updateassets ua WHERE " + Scope("ua") + " AND ua.storedutc IS NOT NULL AND ua.storedutc < b.e)";
                case "storage.exports":
                    return "(SELECT COALESCE(SUM(ax.sizebytes), 0) FROM accountexports ax WHERE " + Scope("ax") + " AND ax.sizebytes IS NOT NULL AND ax.completedutc IS NOT NULL AND ax.completedutc < b.e " +
                        "AND (ax.expiresutc IS NULL OR ax.expiresutc > b.e))";
                default:
                    throw new ArgumentException("Unknown storage meter " + meter + ".", nameof(meter));
            }
        }

        private string Active()
        {
            return "(SELECT COUNT(DISTINCT au.userid) FROM (" +
                "SELECT ua.userid FROM usageactivity ua WHERE " + Scope("ua") + " AND ua.hourutc >= b.s AND ua.hourutc < b.e UNION " +
                "SELECT se.userid FROM authsessions se WHERE " + Scope("se") + " AND se.principalkind = 'Member' AND ((se.createdutc >= b.s AND se.createdutc < b.e) OR (se.lastusedutc >= b.s AND se.lastusedutc < b.e))" +
                ") au)";
        }

        private string Ai(string column, bool hourly)
        {
            return "(SELECT COALESCE(SUM(r." + column + "), 0) FROM aiusagerollups r WHERE " + Scope("r") + " AND r.istest = " + D.Bool(false) + " AND r.periodstartutc >= b.s AND r.periodstartutc < b.e" +
                (hourly ? " AND r.granularity = " + D.Enum(AiUsageGranularityEnum.Hour) : String.Empty) + ")";
        }

        private string Recorded(string meter)
        {
            return "(SELECT COALESCE(SUM(h.value), 0) FROM usagehourly h WHERE " + Scope("h") + " AND h.meter = " + D.Str(meter) + " AND h.periodstartutc >= b.s AND h.periodstartutc < b.e)";
        }

        private string Expression(string meter, bool hourly)
        {
            string reactions = Created("reactions", "rx", "createdutc", String.Empty);
            string votes = Created("pollvotes", "pv", "createdutc", String.Empty);
            string follows = Created("forumfollows", "ff", "createdutc", String.Empty) + " + " + Created("threadfollows", "tf", "createdutc", " AND tf.source = " + D.Enum(FollowSourceEnum.Explicit));
            string assistant = Created("aimessages", "am", "createdutc", " AND am.role = 'User'");
            switch (meter)
            {
                case "users.new": return Created("users", "u", "createdutc", String.Empty);
                case "users.deleted": return Created("users", "u", "deletedutc", String.Empty);
                case "users.total":
                    return "(SELECT COUNT(*) FROM users u WHERE " + Scope("u") + " AND u.createdutc < b.e AND (u.isdeleted = " + D.Bool(false) + " OR (u.deletedutc IS NOT NULL AND u.deletedutc >= b.e)))";
                case "users.active":
                case "users.activeWeekly":
                case "users.activeMonthly":
                    return Active();
                case "posts.new": return Created("feedposts", "fp", "createdutc", String.Empty) + " + " + Created("forumthreads", "ft", "createdutc", String.Empty);
                case "posts.total": return Live("feedposts", "fp", String.Empty) + " + " + Live("forumthreads", "ft", String.Empty);
                case "comments.new": return Created("comments", "c", "createdutc", String.Empty);
                case "comments.total": return Live("comments", "c", String.Empty);
                case "reactions.new": return reactions;
                case "pollVotes.new": return votes;
                case "follows.new": return follows;
                case "assistantMessages.new": return assistant;
                case "interactions.new": return reactions + " + " + votes + " + " + follows + " + " + assistant;
                case "images.new": return MediaFlow(MediaKindEnum.Image, false);
                case "images.bytes": return MediaFlow(MediaKindEnum.Image, true);
                case "videos.new": return MediaFlow(MediaKindEnum.Video, false);
                case "videos.bytes": return MediaFlow(MediaKindEnum.Video, true);
                case "images.total": return "(SELECT COUNT(*) FROM mediaattachments m WHERE " + Scope("m") + " AND m.kind = " + D.Enum(MediaKindEnum.Image) + " AND " + MediaLiveCondition("m") + ")";
                case "videos.total": return "(SELECT COUNT(*) FROM mediaattachments m WHERE " + Scope("m") + " AND m.kind = " + D.Enum(MediaKindEnum.Video) + " AND " + MediaLiveCondition("m") + ")";
                case "storage.total": return String.Join(" + ", UsageMeters.StorageClasses.Select(Storage));
                case "storage.originals":
                case "storage.imageVariants":
                case "storage.videoRenditions":
                case "storage.updateAssets":
                case "storage.exports":
                    return Storage(meter);
                case "api.requests":
                case "api.errors":
                    return Recorded(meter);
                case "push.sent":
                    return "(SELECT COUNT(*) FROM pushdeliveries pd WHERE " + Scope("pd") + " AND pd.status IN " + D.InList(new[] { PushDeliveryStatusEnum.Sent.ToString(), PushDeliveryStatusEnum.Ok.ToString() }) +
                        " AND pd.createdutc >= b.s AND pd.createdutc < b.e)";
                case "email.sent":
                    return Created("emailmessages", "em", "sentutc", String.Empty) + " + " + Created("emailsends", "es", "createdutc", " AND es.status = " + D.Enum(EmailSendStatusEnum.Sent));
                case "ai.requests": return Ai("requests", hourly);
                case "ai.promptTokens": return Ai("prompttokens", hourly);
                case "ai.completionTokens": return Ai("completiontokens", hourly);
                case "ai.costMicros": return Ai("costmicros", hourly);
                default:
                    throw new ArgumentException("Unknown usage meter " + meter + ".", nameof(meter));
            }
        }

        private UsageRecord MapRecord(DataRow row)
        {
            string meter = RowReader.GetString(row, "meter");
            UsageMeter? definition = UsageMeters.Find(meter);
            return new UsageRecord
            {
                Id = RowReader.GetString(row, "id"),
                PropertyId = PropertyId,
                Meter = meter,
                Group = definition?.Group ?? String.Empty,
                Kind = definition?.Kind ?? UsageMeterKindEnum.Count,
                Unit = definition?.Unit ?? "count",
                Day = RowReader.GetString(row, "day"),
                TimeZone = RowReader.GetString(row, "timezone"),
                PeriodStartUtc = DateTime.SpecifyKind(RowReader.GetDateTime(row, "periodstartutc"), DateTimeKind.Utc),
                PeriodEndUtc = DateTime.SpecifyKind(RowReader.GetDateTime(row, "periodendutc"), DateTimeKind.Utc),
                Value = RowReader.GetLong(row, "value"),
                Closed = RowReader.GetBool(row, "closed"),
                ComputedUtc = DateTime.SpecifyKind(RowReader.GetDateTime(row, "computedutc"), DateTimeKind.Utc),
                CreatedUtc = DateTime.SpecifyKind(RowReader.GetDateTime(row, "createdutc"), DateTimeKind.Utc)
            };
        }

        private static UsageMediaItem MapMedia(DataRow row)
        {
            MediaKindEnum kind = RowReader.GetEnum(row, "kind", MediaKindEnum.Image);
            return new UsageMediaItem
            {
                MediaId = RowReader.GetString(row, "id"),
                SizeBytes = RowReader.GetLong(row, "sizebytes"),
                SourceKey = RowReader.GetString(row, "sourcekey"),
                SourceLocation = kind == MediaKindEnum.Video ? MediaLocationEnum.VideoSource : MediaLocationEnum.Source
            };
        }

        private static DateTime? Utc(DateTime? value)
        {
            return value == null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
        }
    }
}
