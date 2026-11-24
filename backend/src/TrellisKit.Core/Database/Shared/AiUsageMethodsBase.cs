namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared usage rollup, cost, budget, and analytics methods.
    /// </summary>
    internal abstract class AiUsageMethodsBase : MethodsBase, IAiUsageMethods
    {
        private static readonly long[] _BucketLimits = new long[] { 250, 500, 1000, 2000, 5000, 10000, 30000, 60000 };
        private static readonly string[] _BucketColumns = new[] { "bucket250", "bucket500", "bucket1000", "bucket2000", "bucket5000", "bucket10000", "bucket30000", "bucket60000", "bucketover" };

        private const string _SumColumns =
            "COALESCE(SUM(r.requests), 0) AS requests, COALESCE(SUM(r.errors), 0) AS errors, COALESCE(SUM(r.prompttokens), 0) AS prompttokens, " +
            "COALESCE(SUM(r.completiontokens), 0) AS completiontokens, COALESCE(SUM(r.reasoningtokens), 0) AS reasoningtokens, COALESCE(SUM(r.costmicros), 0) AS costmicros, " +
            "COALESCE(SUM(r.latencymssum), 0) AS latencymssum, COALESCE(MAX(r.latencymsmax), 0) AS latencymsmax, COALESCE(SUM(r.ttftmssum), 0) AS ttftmssum, " +
            "COALESCE(SUM(r.ttftcount), 0) AS ttftcount, COALESCE(SUM(r.bucket250), 0) AS bucket250, COALESCE(SUM(r.bucket500), 0) AS bucket500, " +
            "COALESCE(SUM(r.bucket1000), 0) AS bucket1000, COALESCE(SUM(r.bucket2000), 0) AS bucket2000, COALESCE(SUM(r.bucket5000), 0) AS bucket5000, " +
            "COALESCE(SUM(r.bucket10000), 0) AS bucket10000, COALESCE(SUM(r.bucket30000), 0) AS bucket30000, COALESCE(SUM(r.bucket60000), 0) AS bucket60000, " +
            "COALESCE(SUM(r.bucketover), 0) AS bucketover";

        protected AiUsageMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<bool> ApplyAsync(AiUsageRecord record, string? guardKey, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            AiUsageRecord u = record;
            DateTime hour = new DateTime(u.OccurredUtc.Year, u.OccurredUtc.Month, u.OccurredUtc.Day, u.OccurredUtc.Hour, 0, 0, DateTimeKind.Utc);
            int bucket = BucketIndex(u.LatencyMs);
            bool isError = u.Outcome != AiUsageOutcomeEnum.Ok && u.Outcome != AiUsageOutcomeEnum.Blocked && u.Outcome != AiUsageOutcomeEnum.Cancelled;
            string values = Pid + ", " + D.Ts(hour) + ", " + D.Enum(AiUsageGranularityEnum.Hour) + ", " + D.Str(u.AssistantId ?? String.Empty) + ", " + D.Str(u.EndpointId ?? String.Empty) + ", " +
                D.Enum(u.Purpose) + ", " + D.Enum(u.Outcome) + ", " + D.Bool(u.IsTest) + ", 1, " + (isError ? "1" : "0") + ", " + D.Num(u.PromptTokens) + ", " + D.Num(u.CompletionTokens) + ", " +
                D.Num(u.ReasoningTokens) + ", " + D.Num(u.CostMicros) + ", " + D.Num(u.LatencyMs) + ", " + D.Num(u.LatencyMs) + ", " + D.Num(u.TimeToFirstTokenMs ?? 0) + ", " +
                (u.TimeToFirstTokenMs != null ? "1" : "0") + ", " + String.Join(", ", _BucketColumns.Select((c, i) => i == bucket ? "1" : "0"));
            string columns = "propertyid, periodstartutc, granularity, assistantid, endpointid, purpose, outcome, istest, requests, errors, prompttokens, completiontokens, reasoningtokens, costmicros, " +
                "latencymssum, latencymsmax, ttftmssum, ttftcount, " + String.Join(", ", _BucketColumns);
            string conflict = " ON CONFLICT (propertyid, periodstartutc, granularity, assistantid, endpointid, purpose, outcome, istest) DO UPDATE SET requests = aiusagerollups.requests + EXCLUDED.requests, " +
                "errors = aiusagerollups.errors + EXCLUDED.errors, prompttokens = aiusagerollups.prompttokens + EXCLUDED.prompttokens, completiontokens = aiusagerollups.completiontokens + EXCLUDED.completiontokens, " +
                "reasoningtokens = aiusagerollups.reasoningtokens + EXCLUDED.reasoningtokens, costmicros = aiusagerollups.costmicros + EXCLUDED.costmicros, " +
                "latencymssum = aiusagerollups.latencymssum + EXCLUDED.latencymssum, latencymsmax = GREATEST(aiusagerollups.latencymsmax, EXCLUDED.latencymsmax), " +
                "ttftmssum = aiusagerollups.ttftmssum + EXCLUDED.ttftmssum, ttftcount = aiusagerollups.ttftcount + EXCLUDED.ttftcount, " +
                String.Join(", ", _BucketColumns.Select(c => c + " = aiusagerollups." + c + " + EXCLUDED." + c));
            string sql;
            if (String.IsNullOrEmpty(guardKey))
            {
                sql = "INSERT INTO aiusagerollups (" + columns + ") VALUES (" + values + ")" + conflict + " RETURNING propertyid;";
            }
            else
            {
                sql = "WITH g AS (INSERT INTO aiusageapplied (propertyid, messageid, purpose, createdutc) VALUES (" + Pid + ", " + D.Str(guardKey) + ", " + D.Enum(u.Purpose) + ", " + D.Ts(u.OccurredUtc) +
                    ") ON CONFLICT DO NOTHING RETURNING propertyid) INSERT INTO aiusagerollups (" + columns + ") SELECT " + values + " WHERE EXISTS (SELECT 1 FROM g)" + conflict + " RETURNING propertyid;";
            }
            DataTable result = await QueryAsync(sql, token).ConfigureAwait(false);
            return result.Rows.Count > 0;
        }

        public async Task<AiUsageRollup> TotalsAsync(DateTime sinceUtc, string? assistantId, string? endpointId, bool includeTest, CancellationToken token = default)
        {
            List<string> conditions = WindowConditions(sinceUtc, assistantId, endpointId);
            if (!includeTest) conditions.Add("r.istest = " + D.Bool(false));
            DataTable table = await QueryAsync("SELECT " + _SumColumns + " FROM aiusagerollups r WHERE " + String.Join(" AND ", conditions) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? new AiUsageRollup() : MapSums(table.Rows[0]);
        }

        public async Task<List<AiUsageRollup>> SeriesAsync(DateTime sinceUtc, AiUsageGranularityEnum granularity, string? assistantId, string? endpointId, CancellationToken token = default)
        {
            List<string> conditions = WindowConditions(sinceUtc, assistantId, endpointId);
            conditions.Add("r.istest = " + D.Bool(false));
            string period = granularity == AiUsageGranularityEnum.Hour ? "date_trunc('hour', r.periodstartutc AT TIME ZONE 'UTC')" : "date_trunc('day', r.periodstartutc AT TIME ZONE 'UTC')";
            DataTable table = await QueryAsync("SELECT " + period + " AS period, " + _SumColumns + " FROM aiusagerollups r WHERE " + String.Join(" AND ", conditions) +
                " GROUP BY 1 ORDER BY 1 LIMIT 400;", token).ConfigureAwait(false);
            List<AiUsageRollup> rows = new List<AiUsageRollup>();
            foreach (DataRow row in table.Rows)
            {
                AiUsageRollup item = MapSums(row);
                DateTime period2 = RowReader.GetDateTime(row, "period");
                item.PeriodStartUtc = DateTime.SpecifyKind(period2, DateTimeKind.Utc);
                item.Granularity = granularity;
                rows.Add(item);
            }
            return rows;
        }

        public async Task<List<AiUsageRollup>> PurposesAsync(IEnumerable<string> endpointIds, DateTime sinceUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(endpointIds);
            List<string> ids = endpointIds.Where(i => !String.IsNullOrEmpty(i)).Distinct().Take(500).ToList();
            if (ids.Count == 0) return new List<AiUsageRollup>();
            DataTable table = await QueryAsync("SELECT r.endpointid, r.purpose, COALESCE(SUM(r.requests), 0) AS requests FROM aiusagerollups r WHERE " + Scope("r") + " AND r.endpointid IN " + D.InList(ids) +
                " AND r.periodstartutc >= " + D.Ts(sinceUtc) + " AND r.istest = " + D.Bool(false) + " GROUP BY r.endpointid, r.purpose;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new AiUsageRollup
            {
                EndpointId = RowReader.GetString(row, "endpointid"),
                Purpose = RowReader.GetString(row, "purpose"),
                Requests = RowReader.GetLong(row, "requests")
            }).ToList();
        }

        public Task<EnumerationResult<AiUsageRollup>> EnumerateEndpointTotalsAsync(EnumerationRequest request, DateTime sinceUtc, string? assistantId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string assistant = String.IsNullOrEmpty(assistantId) ? String.Empty : " AND r.assistantid = " + D.Str(assistantId);
            string from = "aiendpoints e LEFT JOIN LATERAL (SELECT " + _SumColumns + " FROM aiusagerollups r WHERE r.propertyid = e.propertyid AND r.endpointid = e.id AND r.istest = " + D.Bool(false) +
                " AND r.periodstartutc >= " + D.Ts(sinceUtc) + assistant + ") u ON TRUE";
            string select = "e.id, e.name, e.apitype, e.model, e.deletedutc, " + String.Join(", ", SumNames().Select(n => "u." + n));
            return EnumerateAsync(request, select, from, new List<string>(), row =>
            {
                AiUsageRollup item = MapSums(row);
                item.EndpointId = RowReader.GetString(row, "id");
                item.Purpose = RowReader.GetString(row, "name") + "|" + RowReader.GetString(row, "apitype") + "|" + RowReader.GetString(row, "model") + "|" +
                    (RowReader.GetNullableDateTime(row, "deletedutc") != null ? "deleted" : "live");
                return item;
            }, token);
        }

        public Task<EnumerationResult<AiUsageRollup>> EnumerateAssistantTotalsAsync(EnumerationRequest request, DateTime sinceUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string from = "aiassistants a LEFT JOIN LATERAL (SELECT " + _SumColumns + " FROM aiusagerollups r WHERE r.propertyid = a.propertyid AND r.assistantid = a.id AND r.istest = " + D.Bool(false) +
                " AND r.periodstartutc >= " + D.Ts(sinceUtc) + ") u ON TRUE";
            string select = "a.id, a.name, a.deletedutc, " + String.Join(", ", SumNames().Select(n => "u." + n));
            return EnumerateAsync(request, select, from, new List<string>(), row =>
            {
                AiUsageRollup item = MapSums(row);
                item.AssistantId = RowReader.GetString(row, "id");
                item.Purpose = RowReader.GetString(row, "name") + "|" + (RowReader.GetNullableDateTime(row, "deletedutc") != null ? "deleted" : "live");
                return item;
            }, token);
        }

        public Task<EnumerationResult<AiDailyUsage>> EnumerateMemberTotalsAsync(EnumerationRequest request, string sinceDate, AiEnumerationFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> inner = new List<string> { "x.propertyid = " + Pid, "x.date >= " + D.Str(sinceDate) };
            if (!String.IsNullOrEmpty(filter.AssistantId)) inner.Add("x.assistantid = " + D.Str(filter.AssistantId));
            if (!String.IsNullOrEmpty(filter.SubjectType)) inner.Add("x.subjecttype = " + D.Str(filter.SubjectType));
            string from = "(SELECT x.propertyid, x.subjecttype || ':' || x.subjectid AS subjectkey, x.subjecttype, x.subjectid, SUM(x.messages) AS messages, SUM(x.prompttokens) AS prompttokens, " +
                "SUM(x.completiontokens) AS completiontokens, SUM(x.costmicros) AS costmicros, (MAX(x.date) || 'T00:00:00Z')::timestamptz AS lastdate FROM aidailyusage x WHERE " +
                String.Join(" AND ", inner) + " GROUP BY x.propertyid, x.subjecttype, x.subjectid) d";
            return EnumerateAsync(request, "d.subjecttype, d.subjectid, d.messages, d.prompttokens, d.completiontokens, d.costmicros, d.lastdate", from, new List<string>(), row => new AiDailyUsage
            {
                SubjectType = RowReader.GetString(row, "subjecttype"),
                SubjectId = RowReader.GetString(row, "subjectid"),
                Messages = (int)Math.Min(Int32.MaxValue, RowReader.GetLong(row, "messages")),
                PromptTokens = RowReader.GetLong(row, "prompttokens"),
                CompletionTokens = RowReader.GetLong(row, "completiontokens"),
                CostMicros = RowReader.GetLong(row, "costmicros"),
                Date = RowReader.GetDateTime(row, "lastdate").ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            }, token);
        }

        public async Task<AiActivityCounts> ActivityAsync(DateTime sinceUtc, string? assistantId, CancellationToken token = default)
        {
            string assistant = String.IsNullOrEmpty(assistantId) ? String.Empty : " AND assistantid = " + D.Str(assistantId);
            string since = D.Ts(sinceUtc);
            string sql = "SELECT " +
                "(SELECT COUNT(*) FROM aiconversations WHERE " + Scope() + " AND createdutc >= " + since + assistant + ") AS conversations, " +
                "(SELECT COUNT(*) FROM aimessages WHERE " + Scope() + " AND role = 'User' AND createdutc >= " + since + assistant + ") AS messages, " +
                "(SELECT COUNT(*) FROM aimessages WHERE " + Scope() + " AND role = 'Assistant' AND issuperseded = " + D.Bool(false) + " AND createdutc >= " + since + assistant + ") AS replies, " +
                "(SELECT COUNT(*) FROM aimessages WHERE " + Scope() + " AND role = 'Assistant' AND status = 'Failed' AND createdutc >= " + since + assistant + ") AS failed, " +
                "(SELECT COUNT(*) FROM aimessages WHERE " + Scope() + " AND role = 'Assistant' AND status = 'Blocked' AND createdutc >= " + since + assistant + ") AS blocked, " +
                "(SELECT COUNT(*) FROM aimessages WHERE " + Scope() + " AND role = 'Assistant' AND usedfallback = " + D.Bool(true) + " AND createdutc >= " + since + assistant + ") AS fallback, " +
                "(SELECT COUNT(DISTINCT userid) FROM aiconversations WHERE " + Scope() + " AND userid IS NOT NULL AND lastmessageutc >= " + since + assistant + ") AS members, " +
                "(SELECT COUNT(DISTINCT visitorid) FROM aiconversations WHERE " + Scope() + " AND visitorid IS NOT NULL AND lastmessageutc >= " + since + assistant + ") AS visitors, " +
                "(SELECT COUNT(*) FROM aifeedback WHERE " + Scope() + " AND rating = 'Up' AND createdutc >= " + since + assistant + ") AS up, " +
                "(SELECT COUNT(*) FROM aifeedback WHERE " + Scope() + " AND rating = 'Down' AND createdutc >= " + since + assistant + ") AS down;";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            DataRow row = table.Rows[0];
            return new AiActivityCounts
            {
                Conversations = RowReader.GetLong(row, "conversations"),
                Messages = RowReader.GetLong(row, "messages"),
                Replies = RowReader.GetLong(row, "replies"),
                FailedReplies = RowReader.GetLong(row, "failed"),
                BlockedReplies = RowReader.GetLong(row, "blocked"),
                FallbackReplies = RowReader.GetLong(row, "fallback"),
                ActiveMembers = RowReader.GetLong(row, "members"),
                Visitors = RowReader.GetLong(row, "visitors"),
                ThumbsUp = RowReader.GetLong(row, "up"),
                ThumbsDown = RowReader.GetLong(row, "down")
            };
        }

        public async Task<Dictionary<string, AiActivityCounts>> ActivityByAssistantAsync(IEnumerable<string> assistantIds, DateTime sinceUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(assistantIds);
            List<string> ids = assistantIds.Where(i => !String.IsNullOrEmpty(i)).Distinct().Take(500).ToList();
            Dictionary<string, AiActivityCounts> result = ids.ToDictionary(i => i, i => new AiActivityCounts(), StringComparer.Ordinal);
            if (ids.Count == 0) return result;
            string list = D.InList(ids);
            string since = D.Ts(sinceUtc);
            DataTable conversations = await QueryAsync("SELECT assistantid, COUNT(*) AS cnt FROM aiconversations WHERE " + Scope() + " AND assistantid IN " + list + " AND createdutc >= " + since +
                " GROUP BY assistantid;", token).ConfigureAwait(false);
            foreach (DataRow row in conversations.Rows) result[RowReader.GetString(row, "assistantid")].Conversations = RowReader.GetLong(row, "cnt");
            DataTable messages = await QueryAsync("SELECT assistantid, COUNT(*) AS cnt FROM aimessages WHERE " + Scope() + " AND role = 'User' AND assistantid IN " + list + " AND createdutc >= " + since +
                " GROUP BY assistantid;", token).ConfigureAwait(false);
            foreach (DataRow row in messages.Rows) result[RowReader.GetString(row, "assistantid")].Messages = RowReader.GetLong(row, "cnt");
            DataTable feedback = await QueryAsync("SELECT assistantid, rating, COUNT(*) AS cnt FROM aifeedback WHERE " + Scope() + " AND assistantid IN " + list + " AND createdutc >= " + since +
                " GROUP BY assistantid, rating;", token).ConfigureAwait(false);
            foreach (DataRow row in feedback.Rows)
            {
                AiActivityCounts counts = result[RowReader.GetString(row, "assistantid")];
                if (RowReader.GetString(row, "rating") == "Up") counts.ThumbsUp = RowReader.GetLong(row, "cnt");
                else counts.ThumbsDown = RowReader.GetLong(row, "cnt");
            }
            return result;
        }

        public async Task<long> CostSinceAsync(DateTime sinceUtc, string? assistantId, CancellationToken token = default)
        {
            string assistant = String.IsNullOrEmpty(assistantId) ? String.Empty : " AND assistantid = " + D.Str(assistantId);
            DataTable table = await QueryAsync("SELECT COALESCE(SUM(costmicros), 0) AS cnt FROM aiusagerollups WHERE " + Scope() + " AND istest = " + D.Bool(false) + " AND periodstartutc >= " +
                D.Ts(sinceUtc) + assistant + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : RowReader.GetLong(table.Rows[0], "cnt");
        }

        public async Task<bool> TryRecordBudgetAlertAsync(string scope, string scopeId, string month, int level, DateTime nowUtc, CancellationToken token = default)
        {
            DataTable result = await QueryAsync("INSERT INTO aibudgetalerts (propertyid, scope, scopeid, month, level, createdutc) VALUES (" + Pid + ", " + D.Str(scope) + ", " + D.Str(scopeId) + ", " +
                D.Str(month) + ", " + D.Num(level) + ", " + D.Ts(nowUtc) + ") ON CONFLICT DO NOTHING RETURNING level;", token).ConfigureAwait(false);
            return result.Rows.Count > 0;
        }

        public async Task<int> CompactAsync(DateTime compactBeforeUtc, DateTime retainAfterUtc, CancellationToken token = default)
        {
            string keys = "periodstartutc, granularity, assistantid, endpointid, purpose, outcome, istest";
            string sums = "requests, errors, prompttokens, completiontokens, reasoningtokens, costmicros, latencymssum, latencymsmax, ttftmssum, ttftcount, " + String.Join(", ", _BucketColumns);
            string sumSelect = "SUM(requests), SUM(errors), SUM(prompttokens), SUM(completiontokens), SUM(reasoningtokens), SUM(costmicros), SUM(latencymssum), MAX(latencymsmax), SUM(ttftmssum), SUM(ttftcount), " +
                String.Join(", ", _BucketColumns.Select(c => "SUM(" + c + ")"));
            string conflict = " ON CONFLICT (propertyid, " + keys + ") DO UPDATE SET " +
                String.Join(", ", sums.Split(',').Select(c => c.Trim()).Select(c => c == "latencymsmax" ? "latencymsmax = GREATEST(aiusagerollups.latencymsmax, EXCLUDED.latencymsmax)" : c + " = aiusagerollups." + c + " + EXCLUDED." + c));
            string before = D.Ts(compactBeforeUtc);
            List<string> statements = new List<string>
            {
                "INSERT INTO aiusagerollups (propertyid, " + keys + ", " + sums + ") SELECT propertyid, date_trunc('day', periodstartutc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC', " + D.Enum(AiUsageGranularityEnum.Day) +
                ", assistantid, endpointid, purpose, outcome, istest, " + sumSelect + " FROM aiusagerollups WHERE " + Scope() + " AND granularity = " + D.Enum(AiUsageGranularityEnum.Hour) +
                " AND periodstartutc < " + before + " GROUP BY propertyid, 2, assistantid, endpointid, purpose, outcome, istest" + conflict + ";",
                "DELETE FROM aiusagerollups WHERE " + Scope() + " AND granularity = " + D.Enum(AiUsageGranularityEnum.Hour) + " AND periodstartutc < " + before + ";",
                "DELETE FROM aiusagerollups WHERE " + Scope() + " AND periodstartutc < " + D.Ts(retainAfterUtc) + ";",
                "DELETE FROM aiusageapplied WHERE " + Scope() + " AND createdutc < " + D.Ts(compactBeforeUtc) + " RETURNING messageid;"
            };
            DataTable result = await TransactionAsync(statements, token).ConfigureAwait(false);
            return result.Rows.Count;
        }

        private List<string> WindowConditions(DateTime sinceUtc, string? assistantId, string? endpointId)
        {
            List<string> conditions = new List<string> { Scope("r"), "r.periodstartutc >= " + D.Ts(sinceUtc) };
            if (!String.IsNullOrEmpty(assistantId)) conditions.Add("r.assistantid = " + D.Str(assistantId));
            if (!String.IsNullOrEmpty(endpointId)) conditions.Add("r.endpointid = " + D.Str(endpointId));
            return conditions;
        }

        private static IEnumerable<string> SumNames()
        {
            return new[] { "requests", "errors", "prompttokens", "completiontokens", "reasoningtokens", "costmicros", "latencymssum", "latencymsmax", "ttftmssum", "ttftcount" }.Concat(_BucketColumns);
        }

        private static int BucketIndex(long latencyMs)
        {
            for (int i = 0; i < _BucketLimits.Length; i++)
                if (latencyMs <= _BucketLimits[i]) return i;
            return _BucketLimits.Length;
        }

        private static AiUsageRollup MapSums(DataRow row)
        {
            return new AiUsageRollup
            {
                Requests = RowReader.GetLong(row, "requests"),
                Errors = RowReader.GetLong(row, "errors"),
                PromptTokens = RowReader.GetLong(row, "prompttokens"),
                CompletionTokens = RowReader.GetLong(row, "completiontokens"),
                ReasoningTokens = RowReader.GetLong(row, "reasoningtokens"),
                CostMicros = RowReader.GetLong(row, "costmicros"),
                LatencyMsSum = RowReader.GetLong(row, "latencymssum"),
                LatencyMsMax = RowReader.GetLong(row, "latencymsmax"),
                TtftMsSum = RowReader.GetLong(row, "ttftmssum"),
                TtftCount = RowReader.GetLong(row, "ttftcount"),
                Buckets = _BucketColumns.Select(c => RowReader.GetLong(row, c)).ToList()
            };
        }
    }
}
