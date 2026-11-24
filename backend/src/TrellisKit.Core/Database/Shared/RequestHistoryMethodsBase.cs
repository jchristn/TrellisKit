namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared request history methods. Providers supply the summary bucket expression and the case-sensitive
    /// substring predicate. Deletes remove the entries' labels and tags in the same transaction.
    /// </summary>
    internal abstract class RequestHistoryMethodsBase : MethodsBase, IRequestHistoryMethods
    {
        private const string _ListColumns =
            "rh.id, rh.userid, rh.principalname, rh.method, rh.path, rh.url, rh.statuscode, rh.durationms, rh.sourceip, " +
            "rh.requestbodybytes, rh.requestbodytruncated, rh.responsebodybytes, rh.responsebodytruncated, rh.createdutc, rh.completedutc";

        protected RequestHistoryMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task CreateAsync(RequestHistoryEntry entry, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            string sql =
                "INSERT INTO requesthistory (propertyid, id, userid, principalname, method, path, url, statuscode, durationms, sourceip, " +
                "requestheaders, requestbody, requestbodybytes, requestbodytruncated, " +
                "responseheaders, responsebody, responsebodybytes, responsebodytruncated, createdutc, completedutc, metricroute, metricerror, metricapp, metricplatform, metricversion) VALUES (" +
                Pid + ", " +
                D.Str(entry.Id) + ", " +
                D.Str(entry.UserId) + ", " +
                D.Str(entry.PrincipalName) + ", " +
                D.Str(entry.Method) + ", " +
                D.Str(entry.Path) + ", " +
                D.Str(entry.Url) + ", " +
                D.Num(entry.StatusCode) + ", " +
                D.Num(entry.DurationMs) + ", " +
                D.Str(entry.SourceIp) + ", " +
                D.Str(JsonSerializer.Serialize(entry.RequestHeaders)) + ", " +
                D.Str(entry.RequestBody) + ", " +
                D.Num(entry.RequestBodyBytes) + ", " +
                D.Bool(entry.RequestBodyTruncated) + ", " +
                D.Str(JsonSerializer.Serialize(entry.ResponseHeaders)) + ", " +
                D.Str(entry.ResponseBody) + ", " +
                D.Num(entry.ResponseBodyBytes) + ", " +
                D.Bool(entry.ResponseBodyTruncated) + ", " +
                D.Ts(entry.CreatedUtc) + ", " +
                D.Ts(entry.CompletedUtc) + ", " + D.Str(entry.MetricRoute) + ", " + D.Str(entry.MetricError) + ", " + D.Str(entry.MetricApp) + ", " + D.Str(entry.MetricPlatform) + ", " + D.Str(entry.MetricVersion) + ");";
            await QueryAsync(sql, token).ConfigureAwait(false);
        }

        public async Task<RequestHistoryEntry?> ReadAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("SELECT * FROM requesthistory WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return MapFull(table.Rows[0]);
        }

        public Task<EnumerationResult<RequestHistoryEntry>> EnumerateAsync(EnumerationRequest request, RequestHistoryFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            return EnumerateAsync(request, _ListColumns, "requesthistory rh", Conditions(filter, "rh."), MapList, token);
        }

        public async Task<RequestHistorySummary> SummarizeAsync(RequestHistoryFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(filter);
            RequestHistorySummaryBuilder.ValidateRange(filter, out DateTime fromUtc, out DateTime toUtc);
            string sql =
                "SELECT " + BucketExpression(fromUtc, filter.BucketMinutes) + " AS bucket, " +
                "SUM(CASE WHEN statuscode < 400 THEN 1 ELSE 0 END) AS successcount, " +
                "SUM(CASE WHEN statuscode >= 400 THEN 1 ELSE 0 END) AS failurecount, " +
                "SUM(durationms) AS durationsum " +
                "FROM requesthistory" + Where(ScopedConditions(filter)) +
                " GROUP BY " + BucketGroupBy + ";";
            DataTable aggregates = await QueryAsync(sql, token).ConfigureAwait(false);
            return RequestHistorySummaryBuilder.Build(fromUtc, toUtc, filter.BucketMinutes, aggregates);
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            List<string> statements = Driver.Metadata.DeleteStatements(TargetTypeEnum.RequestHistory, id);
            statements.Add("DELETE FROM requesthistory WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public Task<int> DeleteManyAsync(RequestHistoryFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(filter);
            return DeleteWhereAsync(Where(ScopedConditions(filter)), token);
        }

        public Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken token = default)
        {
            return DeleteWhereAsync(" WHERE " + Scope() + " AND createdutc < " + D.Ts(olderThanUtc), token);
        }

        protected abstract string BucketExpression(DateTime fromUtc, int bucketMinutes);

        protected abstract string BucketGroupBy { get; }

        protected abstract string ContainsCaseSensitive(string column, string text);

        private async Task<int> DeleteWhereAsync(string where, CancellationToken token)
        {
            List<string> statements = Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.RequestHistory, "SELECT id FROM requesthistory" + where);
            statements.Add("DELETE FROM requesthistory" + where + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private List<string> Conditions(RequestHistoryFilter filter, string alias)
        {
            List<string> conditions = new List<string>();
            if (filter.UserId != null) conditions.Add(alias + "userid = " + D.Str(filter.UserId));
            if (filter.Method != null) conditions.Add(alias + "method = " + D.Str(filter.Method));
            if (filter.StatusCode.HasValue) conditions.Add(alias + "statuscode = " + D.Num(filter.StatusCode.Value));
            if (filter.PathContains != null) conditions.Add(ContainsCaseSensitive(alias + "path", filter.PathContains));
            if (filter.RouteTemplate != null) conditions.Add("CASE WHEN " + alias + "metricroute = '' THEN 'unknown' ELSE " + alias + "metricroute END = " + D.Str(filter.RouteTemplate));
            if (filter.StatusClass != null) conditions.Add("(" + alias + "statuscode / 100) = " + D.Num(Int32.Parse(filter.StatusClass.Substring(0, 1), System.Globalization.CultureInfo.InvariantCulture)));
            if (filter.ErrorCode != null) conditions.Add("CASE WHEN " + alias + "metricerror = '' THEN " + alias + "statuscode::text ELSE " + alias + "metricerror END = " + D.Str(filter.ErrorCode));
            if (filter.ClientApp != null) conditions.Add(alias + "metricapp = " + D.Str(filter.ClientApp));
            if (filter.ClientPlatform != null) conditions.Add(alias + "metricplatform = " + D.Str(filter.ClientPlatform));
            if (filter.ClientVersion != null) conditions.Add(alias + "metricversion = " + D.Str(filter.ClientVersion));

            if (filter.FromUtc.HasValue) conditions.Add(alias + "createdutc >= " + D.Ts(filter.FromUtc.Value));
            if (filter.ToUtc.HasValue) conditions.Add(alias + "createdutc < " + D.Ts(filter.ToUtc.Value));
            return conditions;
        }

        private List<string> ScopedConditions(RequestHistoryFilter filter)
        {
            List<string> conditions = new List<string> { Scope() };
            conditions.AddRange(Conditions(filter, String.Empty));
            return conditions;
        }

        private static Dictionary<string, string> DeserializeHeaders(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            catch (JsonException)
            {
                return new Dictionary<string, string>();
            }
        }

        private static RequestHistoryEntry MapList(DataRow row)
        {
            return new RequestHistoryEntry
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetNullableString(row, "userid"),
                PrincipalName = RowReader.GetNullableString(row, "principalname"),
                Method = RowReader.GetString(row, "method"),
                Path = RowReader.GetString(row, "path"),
                Url = RowReader.GetString(row, "url"),
                StatusCode = RowReader.GetInt(row, "statuscode"),
                DurationMs = RowReader.GetDouble(row, "durationms"),
                SourceIp = RowReader.GetNullableString(row, "sourceip"),
                RequestBodyBytes = RowReader.GetLong(row, "requestbodybytes"),
                RequestBodyTruncated = RowReader.GetBool(row, "requestbodytruncated"),
                ResponseBodyBytes = RowReader.GetLong(row, "responsebodybytes"),
                ResponseBodyTruncated = RowReader.GetBool(row, "responsebodytruncated"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                CompletedUtc = RowReader.GetNullableDateTime(row, "completedutc")
            };
        }

        private static RequestHistoryEntry MapFull(DataRow row)
        {
            RequestHistoryEntry entry = MapList(row);
            entry.RequestHeaders = DeserializeHeaders(RowReader.GetNullableString(row, "requestheaders"));
            entry.RequestBody = RowReader.GetNullableString(row, "requestbody");
            entry.ResponseHeaders = DeserializeHeaders(RowReader.GetNullableString(row, "responseheaders"));
            entry.ResponseBody = RowReader.GetNullableString(row, "responsebody");
            return entry;
        }

        public Task<long> CountSinceAsync(DateTime sinceUtc, int? minStatusCode, CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM requesthistory WHERE " + Scope() + " AND createdutc >= " + D.Ts(sinceUtc) +
                (minStatusCode == null ? String.Empty : " AND statuscode >= " + D.Num(minStatusCode.Value)) + ";", token);
        }
    }
}
