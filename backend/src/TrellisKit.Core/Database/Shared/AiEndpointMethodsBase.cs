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
    /// Shared model endpoint methods.
    /// </summary>
    internal abstract class AiEndpointMethodsBase : MethodsBase, IAiEndpointMethods
    {
        private const string _Columns =
            "id, scope, name, namenormalized, description, enabled, kind, apitype, model, endpointurl, authtype, authheadername, authqueryparameter, authusername, authvaluesecret, " +
            "region, project, apiversion, deployment, accesskeyid, temperature, topp, maxtokens, contextwindow, reasoningeffort, streaming, timeoutms, firstresponsetimeoutms, dimensions, " +
            "maxconcurrentrequests, maxqueuedepth, maxretries, healthcheckenabled, healthcheckmode, healthcheckintervalms, healthchecktimeoutms, healthythreshold, unhealthythreshold, " +
            "healthstatus, lastcheckedutc, lastlatencyms, lasterror, consecutivesuccesses, consecutivefailures, lasthealthyutc, lastunhealthyutc, nextcheckutc, lastunhealthynotifiedutc, " +
            "inputpricepermillion, outputpricepermillion, cachereadpricepermillion, cachewritepricepermillion, toolcalling, deletedutc, createdbyuserid, createdutc, updatedutc";

        protected AiEndpointMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<ModelEndpoint> CreateAsync(ModelEndpoint endpoint, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            endpoint.PropertyId = PropertyId;
            List<string> statements = new List<string> { Insert(endpoint) };
            statements.AddRange(HeaderStatements(endpoint));
            await UniqueTransactionAsync(statements, "An endpoint with that name", token).ConfigureAwait(false);
            return endpoint;
        }

        public async Task<ModelEndpoint?> ReadAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            List<ModelEndpoint> found = await ReadManyAsync(new[] { id }, token).ConfigureAwait(false);
            return found.Count == 0 ? null : found[0];
        }

        public async Task<List<ModelEndpoint>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct().Take(50).ToList();
            if (list.Count == 0) return new List<ModelEndpoint>();
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM aiendpoints WHERE " + Scope() + " AND deletedutc IS NULL AND id IN " + D.InList(list) + ";", token).ConfigureAwait(false);
            List<ModelEndpoint> endpoints = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await AttachHeadersAsync(endpoints, token).ConfigureAwait(false);
            return endpoints;
        }

        public async Task<ModelEndpoint> UpdateAsync(ModelEndpoint endpoint, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ModelEndpoint e = endpoint;
            string sql = "UPDATE aiendpoints SET name = " + D.Str(e.Name) + ", namenormalized = " + D.Str(AiSql.Normalize(e.Name)) + ", description = " + D.Str(e.Description) +
                ", enabled = " + D.Bool(e.Enabled) + ", kind = " + D.Enum(e.Kind) + ", apitype = " + D.Enum(e.ApiType) + ", model = " + D.Str(e.Model) + ", endpointurl = " + D.Str(e.EndpointUrl) +
                ", authtype = " + D.Enum(e.AuthType) + ", authheadername = " + D.Str(e.AuthHeaderName) + ", authqueryparameter = " + D.Str(e.AuthQueryParameter) + ", authusername = " + D.Str(e.AuthUsername) +
                ", authvaluesecret = " + D.Str(e.AuthValueSecret) + ", region = " + D.Str(e.Region) + ", project = " + D.Str(e.Project) + ", apiversion = " + D.Str(e.ApiVersion) +
                ", deployment = " + D.Str(e.Deployment) + ", accesskeyid = " + D.Str(e.AccessKeyId) + ", temperature = " + AiSql.Dbl(D, e.Temperature) + ", topp = " + AiSql.Dbl(D, e.TopP) +
                ", maxtokens = " + D.Num(e.MaxTokens) + ", contextwindow = " + D.Num(e.ContextWindow) + ", reasoningeffort = " + D.Enum(e.ReasoningEffort) + ", streaming = " + D.Bool(e.Streaming) + ", toolcalling = " + D.Enum(e.ToolCalling) +
                ", timeoutms = " + D.Num(e.TimeoutMs) + ", firstresponsetimeoutms = " + D.Num(e.FirstResponseTimeoutMs) + ", dimensions = " + D.Num(e.Dimensions) +
                ", maxconcurrentrequests = " + D.Num(e.MaxConcurrentRequests) + ", maxqueuedepth = " + D.Num(e.MaxQueueDepth) + ", maxretries = " + D.Num(e.MaxRetries) +
                ", healthcheckenabled = " + D.Bool(e.HealthCheckEnabled) + ", healthcheckmode = " + D.Enum(e.HealthCheckMode) + ", healthcheckintervalms = " + D.Num(e.HealthCheckIntervalMs) +
                ", healthchecktimeoutms = " + D.Num(e.HealthCheckTimeoutMs) + ", healthythreshold = " + D.Num(e.HealthyThreshold) + ", unhealthythreshold = " + D.Num(e.UnhealthyThreshold) +
                ", healthstatus = " + D.Enum(e.HealthStatus) + ", nextcheckutc = " + D.Ts(e.NextCheckUtc) +
                ", inputpricepermillion = " + AiSql.Dbl(D, e.InputPricePerMillion) + ", outputpricepermillion = " + AiSql.Dbl(D, e.OutputPricePerMillion) + ", cachereadpricepermillion = " + AiSql.Dbl(D, e.CacheReadPricePerMillion) +
                ", cachewritepricepermillion = " + AiSql.Dbl(D, e.CacheWritePricePerMillion) + ", updatedutc = " + D.Ts(e.UpdatedUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(e.Id) + " AND deletedutc IS NULL;";
            List<string> statements = new List<string> { sql, "DELETE FROM aiendpointheaders WHERE " + Scope() + " AND endpointid = " + D.Str(e.Id) + ";" };
            statements.AddRange(HeaderStatements(e));
            await UniqueTransactionAsync(statements, "An endpoint with that name", token).ConfigureAwait(false);
            return endpoint;
        }

        public Task UpdateHealthAsync(ModelEndpoint endpoint, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ModelEndpoint e = endpoint;
            string sql = "UPDATE aiendpoints SET healthstatus = " + D.Enum(e.HealthStatus) + ", lastcheckedutc = " + D.Ts(e.LastCheckedUtc) + ", lastlatencyms = " + AiSql.Long(D, e.LastLatencyMs) +
                ", lasterror = " + D.Str(e.LastError) + ", consecutivesuccesses = " + D.Num(e.ConsecutiveSuccesses) + ", consecutivefailures = " + D.Num(e.ConsecutiveFailures) +
                ", lasthealthyutc = " + D.Ts(e.LastHealthyUtc) + ", lastunhealthyutc = " + D.Ts(e.LastUnhealthyUtc) + ", nextcheckutc = " + D.Ts(e.NextCheckUtc) +
                ", lastunhealthynotifiedutc = " + D.Ts(e.LastUnhealthyNotifiedUtc) + " WHERE " + Scope() + " AND id = " + D.Str(e.Id) + ";";
            return QueryAsync(sql, token);
        }

        public async Task<bool> DeleteAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable result = await TransactionAsync(new List<string>
            {
                "DELETE FROM aiendpointheaders WHERE " + Scope() + " AND endpointid = " + D.Str(id) + ";",
                "UPDATE aiendpoints SET deletedutc = " + D.Ts(nowUtc) + ", authvaluesecret = NULL, enabled = " + D.Bool(false) + ", updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL RETURNING id;"
            }, token).ConfigureAwait(false);
            return result.Rows.Count > 0;
        }

        public Task<EnumerationResult<ModelEndpoint>> EnumerateAsync(EnumerationRequest request, AiEnumerationFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string> { "e.deletedutc IS NULL" };
            if (filter.Kind != null) conditions.Add("e.kind = " + D.Enum(filter.Kind.Value));
            if (filter.ApiType != null) conditions.Add("e.apitype = " + D.Enum(filter.ApiType.Value));
            if (filter.Enabled != null) conditions.Add("e.enabled = " + D.Bool(filter.Enabled.Value));
            if (filter.HealthStatus != null) conditions.Add("e.healthstatus = " + D.Enum(filter.HealthStatus.Value));
            if (filter.AuthType != null) conditions.Add("e.authtype = " + D.Enum(filter.AuthType.Value));
            if (filter.Scope != null) conditions.Add("e.scope = " + D.Enum(filter.Scope.Value));
            string select = String.Join(", ", _Columns.Split(',').Select(c => "e." + c.Trim()));
            return EnumerateAsync(request, select, "aiendpoints e", conditions, Map, token);
        }

        public async Task<List<ModelEndpoint>> ListDueForHealthAsync(DateTime nowUtc, int limit, CancellationToken token = default)
        {
            int take = Math.Clamp(limit, 1, 500);
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM aiendpoints WHERE " + Scope() + " AND deletedutc IS NULL AND enabled = " + D.Bool(true) +
                " AND healthcheckenabled = " + D.Bool(true) + " AND (nextcheckutc IS NULL OR nextcheckutc <= " + D.Ts(nowUtc) + ") ORDER BY nextcheckutc NULLS FIRST, id LIMIT " + take + ";", token).ConfigureAwait(false);
            List<ModelEndpoint> endpoints = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await AttachHeadersAsync(endpoints, token).ConfigureAwait(false);
            return endpoints;
        }

        public async Task<List<ModelEndpoint>> ListPageAsync(int limit, string? afterId, CancellationToken token = default)
        {
            int take = Math.Clamp(limit, 1, 500);
            string after = String.IsNullOrEmpty(afterId) ? String.Empty : " AND id > " + D.Str(afterId);
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM aiendpoints WHERE " + Scope() + " AND deletedutc IS NULL" + after + " ORDER BY id LIMIT " + take + ";", token).ConfigureAwait(false);
            List<ModelEndpoint> endpoints = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await AttachHeadersAsync(endpoints, token).ConfigureAwait(false);
            return endpoints;
        }

        public async Task<Dictionary<string, long>> CountByHealthAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT healthstatus, COUNT(*) AS cnt FROM aiendpoints WHERE " + Scope() + " AND deletedutc IS NULL AND enabled = " + D.Bool(true) + " GROUP BY healthstatus;", token).ConfigureAwait(false);
            Dictionary<string, long> counts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (DataRow row in table.Rows) counts[RowReader.GetString(row, "healthstatus")] = RowReader.GetLong(row, "cnt");
            return counts;
        }

        public Task<EnumerationResult<EndpointUsageItem>> EnumerateUsageAsync(EnumerationRequest request, string endpointId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireText(endpointId, nameof(endpointId));
            List<string> conditions = new List<string> { Scope("a"), "a.id = am.assistantid", "a.deletedutc IS NULL", "am.endpointid = " + D.Str(endpointId) };
            return EnumerateAsync(request, "am.assistantid, am.purpose, a.name", "aiassistantmodels am, aiassistants a", conditions, row => new EndpointUsageItem
            {
                AssistantId = RowReader.GetString(row, "assistantid"),
                AssistantName = RowReader.GetString(row, "name"),
                Purpose = PurposeName(RowReader.GetString(row, "purpose"))
            }, token);
        }

        public async Task<List<string>> ReferencingAssistantsAsync(string endpointId, int limit, CancellationToken token = default)
        {
            RequireText(endpointId, nameof(endpointId));
            DataTable table = await QueryAsync("SELECT DISTINCT am.assistantid FROM aiassistantmodels am JOIN aiassistants a ON a.propertyid = am.propertyid AND a.id = am.assistantid WHERE " + Scope("am") +
                " AND a.propertyid = " + Pid + " AND a.deletedutc IS NULL AND am.endpointid = " + D.Str(endpointId) + " ORDER BY am.assistantid LIMIT " + Math.Clamp(limit, 1, 100) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "assistantid")).ToList();
        }

        public Task AddHealthCheckAsync(ModelEndpointHealthCheck record, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            return QueryAsync("INSERT INTO aiendpointhealthchecks (propertyid, id, endpointid, checkedutc, success, latencyms, statuscode, error, mode) VALUES (" + Pid + ", " + D.Str(record.Id) + ", " +
                D.Str(record.EndpointId) + ", " + D.Ts(record.CheckedUtc) + ", " + D.Bool(record.Success) + ", " + D.Num(record.LatencyMs) + ", " + D.Num(record.StatusCode) + ", " +
                D.Str(record.Error) + ", " + D.Enum(record.Mode) + ");", token);
        }

        public Task<EnumerationResult<ModelEndpointHealthCheck>> EnumerateHealthAsync(EnumerationRequest request, string endpointId, AiEnumerationFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            RequireText(endpointId, nameof(endpointId));
            List<string> conditions = new List<string> { "h.endpointid = " + D.Str(endpointId) };
            if (filter.Success != null) conditions.Add("h.success = " + D.Bool(filter.Success.Value));
            return EnumerateAsync(request, "h.id, h.endpointid, h.checkedutc, h.success, h.latencyms, h.statuscode, h.error, h.mode", "aiendpointhealthchecks h", conditions, MapHealth, token);
        }

        public async Task<long[]> UptimeAsync(string endpointId, DateTime sinceUtc, CancellationToken token = default)
        {
            RequireText(endpointId, nameof(endpointId));
            DataTable table = await QueryAsync("SELECT COUNT(*) AS cnt, COALESCE(SUM(CASE WHEN success THEN 1 ELSE 0 END), 0) AS ok FROM aiendpointhealthchecks WHERE " + Scope() +
                " AND endpointid = " + D.Str(endpointId) + " AND checkedutc >= " + D.Ts(sinceUtc) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return new long[] { 0, 0 };
            return new long[] { RowReader.GetLong(table.Rows[0], "ok"), RowReader.GetLong(table.Rows[0], "cnt") };
        }

        public async Task<int> PruneHealthAsync(DateTime olderThanUtc, int maxPerEndpoint, CancellationToken token = default)
        {
            int keep = Math.Max(1, maxPerEndpoint);
            DataTable aged = await QueryAsync("DELETE FROM aiendpointhealthchecks WHERE " + Scope() + " AND checkedutc < " + D.Ts(olderThanUtc) + " RETURNING id;", token).ConfigureAwait(false);
            DataTable excess = await QueryAsync("DELETE FROM aiendpointhealthchecks WHERE " + Scope() + " AND id IN (SELECT id FROM (SELECT x.id, ROW_NUMBER() OVER (PARTITION BY x.endpointid ORDER BY x.checkedutc DESC) AS rn " +
                "FROM aiendpointhealthchecks x WHERE x.propertyid = " + Pid + ") ranked WHERE ranked.rn > " + keep + ") RETURNING id;", token).ConfigureAwait(false);
            return aged.Rows.Count + excess.Rows.Count;
        }

        protected static string PurposeName(string stored)
        {
            if (!Enum.TryParse(stored, out AssistantPurposeEnum purpose)) return stored;
            string name = purpose.ToString();
            return Char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        private async Task AttachHeadersAsync(List<ModelEndpoint> endpoints, CancellationToken token)
        {
            if (endpoints.Count == 0) return;
            DataTable table = await QueryAsync("SELECT endpointid, name, valuesecret, position FROM aiendpointheaders WHERE " + Scope() + " AND endpointid IN " + D.InList(endpoints.Select(e => e.Id)) +
                " ORDER BY endpointid, position;", token).ConfigureAwait(false);
            Dictionary<string, ModelEndpoint> byId = endpoints.ToDictionary(e => e.Id, StringComparer.Ordinal);
            foreach (DataRow row in table.Rows)
            {
                if (!byId.TryGetValue(RowReader.GetString(row, "endpointid"), out ModelEndpoint? endpoint)) continue;
                endpoint.Headers.Add(new ModelEndpointHeader
                {
                    Name = RowReader.GetString(row, "name"),
                    ValueSecret = RowReader.GetNullableString(row, "valuesecret"),
                    Position = RowReader.GetInt(row, "position")
                });
            }
        }

        private List<string> HeaderStatements(ModelEndpoint e)
        {
            List<string> statements = new List<string>();
            int position = 0;
            foreach (ModelEndpointHeader header in e.Headers)
            {
                statements.Add("INSERT INTO aiendpointheaders (propertyid, endpointid, namenormalized, name, valuesecret, position) VALUES (" + Pid + ", " + D.Str(e.Id) + ", " +
                    D.Str(AiSql.Normalize(header.Name)) + ", " + D.Str(header.Name) + ", " + D.Str(header.ValueSecret) + ", " + D.Num(position++) + ");");
            }
            return statements;
        }

        private string Insert(ModelEndpoint e)
        {
            return "INSERT INTO aiendpoints (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + D.Str(e.Id) + ", " + D.Enum(e.Scope) + ", " + D.Str(e.Name) + ", " + D.Str(AiSql.Normalize(e.Name)) + ", " +
                D.Str(e.Description) + ", " + D.Bool(e.Enabled) + ", " + D.Enum(e.Kind) + ", " + D.Enum(e.ApiType) + ", " + D.Str(e.Model) + ", " + D.Str(e.EndpointUrl) + ", " + D.Enum(e.AuthType) + ", " +
                D.Str(e.AuthHeaderName) + ", " + D.Str(e.AuthQueryParameter) + ", " + D.Str(e.AuthUsername) + ", " + D.Str(e.AuthValueSecret) + ", " + D.Str(e.Region) + ", " + D.Str(e.Project) + ", " +
                D.Str(e.ApiVersion) + ", " + D.Str(e.Deployment) + ", " + D.Str(e.AccessKeyId) + ", " + AiSql.Dbl(D, e.Temperature) + ", " + AiSql.Dbl(D, e.TopP) + ", " + D.Num(e.MaxTokens) + ", " +
                D.Num(e.ContextWindow) + ", " + D.Enum(e.ReasoningEffort) + ", " + D.Bool(e.Streaming) + ", " + D.Num(e.TimeoutMs) + ", " + D.Num(e.FirstResponseTimeoutMs) + ", " + D.Num(e.Dimensions) + ", " +
                D.Num(e.MaxConcurrentRequests) + ", " + D.Num(e.MaxQueueDepth) + ", " + D.Num(e.MaxRetries) + ", " + D.Bool(e.HealthCheckEnabled) + ", " + D.Enum(e.HealthCheckMode) + ", " +
                D.Num(e.HealthCheckIntervalMs) + ", " + D.Num(e.HealthCheckTimeoutMs) + ", " + D.Num(e.HealthyThreshold) + ", " + D.Num(e.UnhealthyThreshold) + ", " + D.Enum(e.HealthStatus) + ", " +
                D.Ts(e.LastCheckedUtc) + ", " + AiSql.Long(D, e.LastLatencyMs) + ", " + D.Str(e.LastError) + ", " + D.Num(e.ConsecutiveSuccesses) + ", " + D.Num(e.ConsecutiveFailures) + ", " +
                D.Ts(e.LastHealthyUtc) + ", " + D.Ts(e.LastUnhealthyUtc) + ", " + D.Ts(e.NextCheckUtc) + ", " + D.Ts(e.LastUnhealthyNotifiedUtc) + ", " + AiSql.Dbl(D, e.InputPricePerMillion) + ", " +
                AiSql.Dbl(D, e.OutputPricePerMillion) + ", " + AiSql.Dbl(D, e.CacheReadPricePerMillion) + ", " + AiSql.Dbl(D, e.CacheWritePricePerMillion) + ", " + D.Enum(e.ToolCalling) + ", " + D.Ts(e.DeletedUtc) + ", " + D.Str(e.CreatedByUserId) + ", " + D.Ts(e.CreatedUtc) + ", " + D.Ts(e.UpdatedUtc) + ");";
        }

        private ModelEndpoint Map(DataRow row)
        {
            return new ModelEndpoint
            {
                PropertyId = PropertyId,
                Id = RowReader.GetString(row, "id"),
                Scope = RowReader.GetEnum(row, "scope", ModelEndpointScopeEnum.Property),
                Name = RowReader.GetString(row, "name"),
                Description = RowReader.GetNullableString(row, "description"),
                Enabled = RowReader.GetBool(row, "enabled"),
                Kind = RowReader.GetEnum(row, "kind", ModelEndpointKindEnum.Completion),
                ApiType = RowReader.GetEnum(row, "apitype", ModelApiTypeEnum.Ollama),
                Model = RowReader.GetString(row, "model"),
                EndpointUrl = RowReader.GetString(row, "endpointurl"),
                AuthType = RowReader.GetEnum(row, "authtype", ModelAuthTypeEnum.None),
                AuthHeaderName = RowReader.GetNullableString(row, "authheadername"),
                AuthQueryParameter = RowReader.GetNullableString(row, "authqueryparameter"),
                AuthUsername = RowReader.GetNullableString(row, "authusername"),
                AuthValueSecret = RowReader.GetNullableString(row, "authvaluesecret"),
                Region = RowReader.GetNullableString(row, "region"),
                Project = RowReader.GetNullableString(row, "project"),
                ApiVersion = RowReader.GetNullableString(row, "apiversion"),
                Deployment = RowReader.GetNullableString(row, "deployment"),
                AccessKeyId = RowReader.GetNullableString(row, "accesskeyid"),
                Temperature = AiSql.NullableDouble(row, "temperature"),
                TopP = AiSql.NullableDouble(row, "topp"),
                MaxTokens = RowReader.GetNullableInt(row, "maxtokens"),
                ContextWindow = RowReader.GetNullableInt(row, "contextwindow"),
                ReasoningEffort = RowReader.GetNullableEnum<ModelReasoningEffortEnum>(row, "reasoningeffort"),
                Streaming = RowReader.GetBool(row, "streaming"),
                ToolCalling = RowReader.GetEnum(row, "toolcalling", ModelToolCallingEnum.Native),
                TimeoutMs = RowReader.GetInt(row, "timeoutms"),
                FirstResponseTimeoutMs = RowReader.GetNullableInt(row, "firstresponsetimeoutms"),
                Dimensions = RowReader.GetNullableInt(row, "dimensions"),
                MaxConcurrentRequests = RowReader.GetInt(row, "maxconcurrentrequests"),
                MaxQueueDepth = RowReader.GetInt(row, "maxqueuedepth"),
                MaxRetries = RowReader.GetInt(row, "maxretries"),
                HealthCheckEnabled = RowReader.GetBool(row, "healthcheckenabled"),
                HealthCheckMode = RowReader.GetEnum(row, "healthcheckmode", ModelHealthCheckModeEnum.Connectivity),
                HealthCheckIntervalMs = RowReader.GetInt(row, "healthcheckintervalms"),
                HealthCheckTimeoutMs = RowReader.GetInt(row, "healthchecktimeoutms"),
                HealthyThreshold = RowReader.GetInt(row, "healthythreshold"),
                UnhealthyThreshold = RowReader.GetInt(row, "unhealthythreshold"),
                HealthStatus = RowReader.GetEnum(row, "healthstatus", ModelHealthStatusEnum.Unknown),
                LastCheckedUtc = RowReader.GetNullableDateTime(row, "lastcheckedutc"),
                LastLatencyMs = RowReader.GetNullableLong(row, "lastlatencyms"),
                LastError = RowReader.GetNullableString(row, "lasterror"),
                ConsecutiveSuccesses = RowReader.GetInt(row, "consecutivesuccesses"),
                ConsecutiveFailures = RowReader.GetInt(row, "consecutivefailures"),
                LastHealthyUtc = RowReader.GetNullableDateTime(row, "lasthealthyutc"),
                LastUnhealthyUtc = RowReader.GetNullableDateTime(row, "lastunhealthyutc"),
                NextCheckUtc = RowReader.GetNullableDateTime(row, "nextcheckutc"),
                LastUnhealthyNotifiedUtc = RowReader.GetNullableDateTime(row, "lastunhealthynotifiedutc"),
                InputPricePerMillion = AiSql.NullableDouble(row, "inputpricepermillion"),
                OutputPricePerMillion = AiSql.NullableDouble(row, "outputpricepermillion"),
                CacheReadPricePerMillion = AiSql.NullableDouble(row, "cachereadpricepermillion"),
                CacheWritePricePerMillion = AiSql.NullableDouble(row, "cachewritepricepermillion"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static ModelEndpointHealthCheck MapHealth(DataRow row)
        {
            return new ModelEndpointHealthCheck
            {
                Id = RowReader.GetString(row, "id"),
                EndpointId = RowReader.GetString(row, "endpointid"),
                CheckedUtc = RowReader.GetDateTime(row, "checkedutc"),
                Success = RowReader.GetBool(row, "success"),
                LatencyMs = RowReader.GetLong(row, "latencyms"),
                StatusCode = RowReader.GetNullableInt(row, "statuscode"),
                Error = RowReader.GetNullableString(row, "error"),
                Mode = RowReader.GetEnum(row, "mode", ModelHealthCheckModeEnum.Connectivity)
            };
        }
    }
}
