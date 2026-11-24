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
    using TrellisKit.Core.Responses;

    internal abstract class AiCallMethodsBase : MethodsBase, IAiCallMethods
    {
        protected AiCallMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public Task InsertAsync(AiCallRecord r, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(r);
            RequireText(r.Id, nameof(r.Id));
            return QueryAsync("INSERT INTO aicalls (propertyid, id, createdutc, purpose, role, assistantid, endpointid, runtime, model, executorposition, escalationreason, outcome, errorcode, isfallback, istest, " +
                "prompttokens, completiontokens, reasoningtokens, latencyms, costmicros, jobkey, jobrunid, jobitemid, " +
                "timetofirsttokenms, timetolasttokenms, streamingms, queuems, tokenspersecond, totaltokens, cachedreadtokens, cachecreationtokens, inputtexttokens, inputimagetokens, inputaudiotokens, inputvideotokens, " +
                "toolcalls, toolexecms, retries, statuscode, finishreason, streamed, chunks, requestbytes, responsebytes, responseid, providermodel, reasoningeffort, loadms, promptevalms, evalms, exitcode) VALUES (" + Pid + ", " + D.Str(r.Id) + ", " + D.Ts(r.OccurredUtc) + ", " + D.Enum(r.Purpose) + ", " +
                D.Str(r.Role) + ", " + D.Str(r.AssistantId) + ", " + D.Str(r.EndpointId) + ", " + D.Enum(r.Runtime) + ", " + D.Str(r.Model) + ", " + D.Num(r.ExecutorPosition) + ", " + D.Str(r.EscalationReason) + ", " +
                D.Enum(r.Outcome) + ", " + D.Str(r.ErrorCode) + ", " + D.Bool(r.IsFallback) + ", " + D.Bool(r.IsTest) + ", " + D.Num(r.PromptTokens) + ", " + D.Num(r.CompletionTokens) + ", " +
                D.Num(r.ReasoningTokens) + ", " + D.Num(r.LatencyMs) + ", " + D.Num(r.CostMicros) + ", " + D.Str(r.JobKey) + ", " + D.Str(r.JobRunId) + ", " + D.Str(r.JobItemId) + ", " +
                AiSql.Long(D, r.Detail.TimeToFirstTokenMs) + ", " + AiSql.Long(D, r.Detail.TimeToLastTokenMs) + ", " + AiSql.Long(D, r.Detail.StreamingMs) + ", " + AiSql.Long(D, r.Detail.QueueMs) + ", " + AiSql.Dbl(D, r.Detail.TokensPerSecond) + ", " + D.Num(r.Detail.TotalTokens) + ", " +
                D.Num(r.Detail.CachedReadTokens) + ", " + D.Num(r.Detail.CacheCreationTokens) + ", " + AiSql.Long(D, r.Detail.InputTextTokens) + ", " + AiSql.Long(D, r.Detail.InputImageTokens) + ", " + AiSql.Long(D, r.Detail.InputAudioTokens) + ", " + AiSql.Long(D, r.Detail.InputVideoTokens) + ", " +
                D.Num(r.Detail.ToolCalls) + ", " + AiSql.Long(D, r.Detail.ToolExecMs) + ", " + D.Num(r.Detail.Retries) + ", " + D.Num(r.Detail.StatusCode) + ", " + D.Str(r.Detail.FinishReason) + ", " + AiSql.Bool(D, r.Detail.Streamed) + ", " +
                D.Num(r.Detail.Chunks) + ", " + AiSql.Long(D, r.Detail.RequestBytes) + ", " + AiSql.Long(D, r.Detail.ResponseBytes) + ", " + D.Str(r.Detail.ResponseId) + ", " + D.Str(r.Detail.ProviderModel) + ", " + D.Str(r.Detail.ReasoningEffort) + ", " +
                AiSql.Long(D, r.Detail.LoadMs) + ", " + AiSql.Long(D, r.Detail.PromptEvalMs) + ", " + AiSql.Long(D, r.Detail.EvalMs) + ", " + D.Num(r.Detail.ExitCode) + ");", token);
        }

        public Task<EnumerationResult<AiCallRecord>> EnumerateAsync(EnumerationRequest request, AiCallFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (filter.Roles.Count > 0) conditions.Add("c.role IN " + D.InList(filter.Roles));
            if (filter.Purposes.Count > 0) conditions.Add("c.purpose IN " + D.InList(filter.Purposes.Select(p => p.ToString())));
            if (filter.Outcomes.Count > 0) conditions.Add("c.outcome IN " + D.InList(filter.Outcomes.Select(o => o.ToString())));
            if (!String.IsNullOrWhiteSpace(filter.EndpointId)) conditions.Add("c.endpointid = " + D.Str(filter.EndpointId.Trim()));
            if (!String.IsNullOrWhiteSpace(filter.JobKey)) conditions.Add("c.jobkey = " + D.Str(filter.JobKey.Trim()));
            if (!String.IsNullOrWhiteSpace(filter.JobRunId)) conditions.Add("c.jobrunid = " + D.Str(filter.JobRunId.Trim()));
            if (!String.IsNullOrWhiteSpace(filter.AssistantId)) conditions.Add("c.assistantid = " + D.Str(filter.AssistantId.Trim()));
            return EnumerateAsync(request, "c.*", "aicalls c", conditions, Map, token);
        }

        public Task SetToolsAsync(string id, int toolCalls, long toolExecMs, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            return QueryAsync("UPDATE aicalls SET toolcalls = " + D.Num(Math.Max(0, toolCalls)) + ", toolexecms = " + D.Num(Math.Max(0L, toolExecMs)) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token);
        }

        public async Task<int> PruneAsync(DateTime beforeUtc, int max, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("DELETE FROM aicalls WHERE " + Scope() + " AND id IN (SELECT o.id FROM aicalls o WHERE " + Scope("o") + " AND o.createdutc < " + D.Ts(beforeUtc) +
                " ORDER BY o.createdutc LIMIT " + Math.Clamp(max, 1, 50000) + ") RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private static AiCallRecord Map(DataRow row)
        {
            return new AiCallRecord
            {
                Id = RowReader.GetString(row, "id"),
                OccurredUtc = RowReader.GetDateTime(row, "createdutc"),
                Purpose = Enum.TryParse(RowReader.GetString(row, "purpose"), out AssistantPurposeEnum p) ? p : AssistantPurposeEnum.Chat,
                Role = RowReader.GetNullableString(row, "role"),
                AssistantId = RowReader.GetNullableString(row, "assistantid"),
                EndpointId = RowReader.GetNullableString(row, "endpointid"),
                Runtime = Enum.TryParse(RowReader.GetString(row, "runtime"), out AiExecutorRuntimeEnum r) ? r : AiExecutorRuntimeEnum.ApiEndpoint,
                Model = RowReader.GetNullableString(row, "model"),
                ExecutorPosition = RowReader.GetNullableInt(row, "executorposition"),
                EscalationReason = RowReader.GetNullableString(row, "escalationreason"),
                Outcome = Enum.TryParse(RowReader.GetString(row, "outcome"), out AiUsageOutcomeEnum o) ? o : AiUsageOutcomeEnum.Ok,
                ErrorCode = RowReader.GetNullableString(row, "errorcode"),
                IsFallback = RowReader.GetBool(row, "isfallback"),
                IsTest = RowReader.GetBool(row, "istest"),
                PromptTokens = RowReader.GetInt(row, "prompttokens"),
                CompletionTokens = RowReader.GetInt(row, "completiontokens"),
                ReasoningTokens = RowReader.GetInt(row, "reasoningtokens"),
                LatencyMs = RowReader.GetLong(row, "latencyms"),
                CostMicros = RowReader.GetLong(row, "costmicros"),
                JobKey = RowReader.GetNullableString(row, "jobkey"),
                JobRunId = RowReader.GetNullableString(row, "jobrunid"),
                JobItemId = RowReader.GetNullableString(row, "jobitemid"),
                Detail = new AiCallDetail
                {
                    TimeToFirstTokenMs = RowReader.GetNullableLong(row, "timetofirsttokenms"),
                    TimeToLastTokenMs = RowReader.GetNullableLong(row, "timetolasttokenms"),
                    StreamingMs = RowReader.GetNullableLong(row, "streamingms"),
                    QueueMs = RowReader.GetNullableLong(row, "queuems"),
                    TokensPerSecond = AiSql.NullableDouble(row, "tokenspersecond"),
                    TotalTokens = RowReader.GetNullableInt(row, "totaltokens"),
                    CachedReadTokens = RowReader.GetNullableInt(row, "cachedreadtokens"),
                    CacheCreationTokens = RowReader.GetNullableInt(row, "cachecreationtokens"),
                    InputTextTokens = RowReader.GetNullableLong(row, "inputtexttokens"),
                    InputImageTokens = RowReader.GetNullableLong(row, "inputimagetokens"),
                    InputAudioTokens = RowReader.GetNullableLong(row, "inputaudiotokens"),
                    InputVideoTokens = RowReader.GetNullableLong(row, "inputvideotokens"),
                    ToolCalls = RowReader.GetNullableInt(row, "toolcalls"),
                    ToolExecMs = RowReader.GetNullableLong(row, "toolexecms"),
                    Retries = RowReader.GetNullableInt(row, "retries"),
                    StatusCode = RowReader.GetNullableInt(row, "statuscode"),
                    FinishReason = RowReader.GetNullableString(row, "finishreason"),
                    Streamed = AiSql.NullableBool(row, "streamed"),
                    Chunks = RowReader.GetNullableInt(row, "chunks"),
                    RequestBytes = RowReader.GetNullableLong(row, "requestbytes"),
                    ResponseBytes = RowReader.GetNullableLong(row, "responsebytes"),
                    ResponseId = RowReader.GetNullableString(row, "responseid"),
                    ProviderModel = RowReader.GetNullableString(row, "providermodel"),
                    ReasoningEffort = RowReader.GetNullableString(row, "reasoningeffort"),
                    LoadMs = RowReader.GetNullableLong(row, "loadms"),
                    PromptEvalMs = RowReader.GetNullableLong(row, "promptevalms"),
                    EvalMs = RowReader.GetNullableLong(row, "evalms"),
                    ExitCode = RowReader.GetNullableInt(row, "exitcode")
                }
            };
        }
    }
}
