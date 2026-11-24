namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Usage rollups, cost, budgets, and analytics (docs/AI_CONTRACT.md section 14 and resolved decision 14): every model
    /// call is added to its hourly rollup row with its admin-priced cost and exported as metrics; per-assistant and
    /// per-property monthly budgets send an administrative email at the alert percent and at 100 percent (once per
    /// month each) and, with the hard stop on, pause member chat.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class AiUsageService
    {
        #region Private-Members

        private static readonly long[] _BucketUpper = new long[] { 250, 500, 1000, 2000, 5000, 10000, 30000, 60000 };
        private readonly ApplicationServices _App;
        private DateTime _NextBudgetCheckUtc = DateTime.MinValue;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="app">Application services of the property.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public AiUsageService(ApplicationServices app)
        {
            _App = app ?? throw new ArgumentNullException(nameof(app));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record one model call: rollup row, metrics, and (for non-test calls) a budget check.
        /// </summary>
        /// <param name="record">Call.</param>
        /// <param name="endpoint">Endpoint, or null.</param>
        /// <param name="fallback">Whether the fallback endpoint was used.</param>
        /// <param name="guardKey">Idempotency key, or null.</param>
        /// <param name="errorCode">Error code of a failed call, or null.</param>
        /// <returns>Identifier of the ledger row (aic_...), or null when the row could not be written.</returns>
        /// <exception cref="ArgumentNullException">The record is null.</exception>
        public async Task<string?> RecordAsync(AiUsageRecord record, ModelEndpoint? endpoint, bool fallback, string? guardKey, string? errorCode = null)
        {
            ArgumentNullException.ThrowIfNull(record);
            string? id = await LedgerAsync(record, record.EndpointId, AiExecutorRuntimeEnum.ApiEndpoint, endpoint?.CallModel, fallback, errorCode).ConfigureAwait(false);
            await RollupAsync(record, AiTelemetry.Label(endpoint?.ApiType.ToString()), fallback, guardKey).ConfigureAwait(false);
            return id;
        }

        /// <summary>
        /// Record a model call that is not member or job traffic (an administrator's endpoint test or validation, or a
        /// health check that calls the model): a ledger row with its telemetry and token metrics, but no usage rollup,
        /// so it never counts toward budgets or analytics.
        /// </summary>
        /// <param name="record">Call (purpose EndpointTest or HealthCheck, marked as test traffic).</param>
        /// <param name="endpoint">Endpoint called.</param>
        /// <param name="errorCode">Error code of a failed call, or null.</param>
        /// <param name="savedEndpoint">Whether the endpoint is saved; an unsaved endpoint (validation) records no endpoint id. Default true.</param>
        /// <returns>Identifier of the ledger row, or null when the row could not be written.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<string?> RecordDirectAsync(AiUsageRecord record, ModelEndpoint endpoint, string? errorCode, bool savedEndpoint = true)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(endpoint);
            record.IsTest = true;
            string? id = await LedgerAsync(record, !savedEndpoint || String.IsNullOrEmpty(endpoint.Id) ? null : endpoint.Id, AiExecutorRuntimeEnum.ApiEndpoint, endpoint.CallModel, false, errorCode).ConfigureAwait(false);
            DetailMetrics(record, AiTelemetry.Label(endpoint.ApiType.ToString()), AiTelemetry.Label(record.Purpose.ToString()));
            return id;
        }

        /// <summary>
        /// Attribute tool execution to the model call that requested the tools (docs/AI_TELEMETRY.md, "Tool time"): the
        /// number of tool calls the engine ran for it and the total time they took. Called by the tool loops after the
        /// tools of a round finish. A failed write is logged and never fails the reply.
        /// </summary>
        /// <param name="callId">Ledger row of the model call, or null (nothing is written).</param>
        /// <param name="endpoint">Endpoint of the model call, or null.</param>
        /// <param name="toolCalls">Tool calls run (0 or more).</param>
        /// <param name="toolExecMs">Total execution time in milliseconds (0 or more).</param>
        /// <returns>Task.</returns>
        public async Task RecordToolsAsync(string? callId, ModelEndpoint? endpoint, int toolCalls, long toolExecMs)
        {
            string apiType = AiTelemetry.Label(endpoint?.ApiType.ToString());
            TrellisKitTelemetry.Record(AiTelemetry.CallToolCalls, Math.Max(0, toolCalls), "api_type", apiType);
            TrellisKitTelemetry.Record(AiTelemetry.CallToolDuration, Math.Max(0, toolExecMs) / 1000.0, "api_type", apiType);
            if (String.IsNullOrEmpty(callId)) return;
            try
            {
                await _App.Database.Ai.Calls.SetToolsAsync(callId, toolCalls, toolExecMs, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _App.Logger.Warn("[AiUsageService] call ledger tool update failed: " + e.Message);
            }
        }

        /// <summary>
        /// Record one CLI agent harness call (docs/HARNESSES.md): a ledger row with the harness runtime and the model it
        /// reported, the rollup row (endpoint key harness:{runtime}, so harness cost counts toward the property's AI
        /// budget and analytics), and metrics with api_type set to the runtime. The cost is added to the ambient scope.
        /// </summary>
        /// <param name="record">Call (purpose, outcome, tokens, latency, cost from the harness's Completed event).</param>
        /// <param name="runtime">Harness runtime.</param>
        /// <param name="model">Model the harness reported or was asked for, or null.</param>
        /// <param name="endpointName">Mux endpoint name, or null.</param>
        /// <param name="errorCode">Error code of a failed call, or null.</param>
        /// <returns>Identifier of the ledger row, or null when the row could not be written.</returns>
        /// <exception cref="ArgumentNullException">The record is null.</exception>
        public async Task<string?> RecordHarnessAsync(AiUsageRecord record, AiExecutorRuntimeEnum runtime, string? model, string? endpointName, string? errorCode)
        {
            ArgumentNullException.ThrowIfNull(record);
            string? id = await LedgerAsync(record, endpointName, runtime, model, false, errorCode).ConfigureAwait(false);
            record.EndpointId = "harness:" + runtime;
            await RollupAsync(record, AiTelemetry.Label(runtime.ToString()), false, null).ConfigureAwait(false);
            return id;
        }

        /// <summary>
        /// Throw 503 BudgetExceeded when the assistant's or the property's monthly budget is spent and its hard stop is
        /// on.
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">503 BudgetExceeded.</exception>
        public async Task EnforceBudgetAsync(Assistant assistant, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            DateTime month = MonthStart(DateTime.UtcNow);
            if (assistant.BudgetHardStop && assistant.MonthlyBudgetUsd != null && assistant.MonthlyBudgetUsd.Value > 0)
            {
                long spent = await _App.Database.Ai.Usage.CostSinceAsync(month, assistant.Id, token).ConfigureAwait(false);
                if (spent >= Micros(assistant.MonthlyBudgetUsd.Value))
                {
                    TrellisKitTelemetry.Add(AiTelemetry.LimitHits, 1, "kind", "budget");
                    throw AiErrors.BudgetExceeded();
                }
            }
            int property = _App.Settings.Ai.MonthlyBudgetUsd;
            if (_App.Settings.Ai.BudgetHardStop && property > 0)
            {
                long spent = await _App.Database.Ai.Usage.CostSinceAsync(month, null, token).ConfigureAwait(false);
                if (spent >= Micros(property))
                {
                    TrellisKitTelemetry.Add(AiTelemetry.LimitHits, 1, "kind", "budget");
                    throw AiErrors.BudgetExceeded();
                }
            }
        }

        /// <summary>
        /// Month-to-date non-test spend in US dollars.
        /// </summary>
        /// <param name="assistantId">Assistant, or null for the property.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Spend.</returns>
        public async Task<double> MonthToDateUsdAsync(string? assistantId, CancellationToken token)
        {
            long micros = await _App.Database.Ai.Usage.CostSinceAsync(MonthStart(DateTime.UtcNow), assistantId, token).ConfigureAwait(false);
            return Usd(micros);
        }

        /// <summary>
        /// GET /api/v1/admin/ai/analytics/overview.
        /// </summary>
        /// <param name="days">Window in days (1 to 365).</param>
        /// <param name="assistantId">Assistant, or null.</param>
        /// <param name="endpointId">Endpoint, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Overview.</returns>
        public async Task<AiOverviewView> OverviewAsync(int days, string? assistantId, string? endpointId, CancellationToken token)
        {
            int window = Math.Clamp(days, 1, 365);
            DateTime since = DateTime.UtcNow.Date.AddDays(-(window - 1));
            AiUsageRollup totals = await _App.Database.Ai.Usage.TotalsAsync(since, assistantId, endpointId, false, token).ConfigureAwait(false);
            AiActivityCounts counts = await _App.Database.Ai.Usage.ActivityAsync(since, assistantId, token).ConfigureAwait(false);
            long rated = counts.ThumbsUp + counts.ThumbsDown;
            return new AiOverviewView
            {
                Days = window,
                Conversations = counts.Conversations,
                Messages = counts.Messages,
                ActiveMembers = counts.ActiveMembers,
                Visitors = counts.Visitors,
                ThumbsUp = counts.ThumbsUp,
                ThumbsDown = counts.ThumbsDown,
                ThumbsUpRate = rated == 0 ? null : Math.Round((double)counts.ThumbsUp / rated, 4),
                Requests = totals.Requests,
                Errors = totals.Errors,
                ErrorRate = totals.Requests == 0 ? 0 : Math.Round((double)totals.Errors / totals.Requests, 4),
                PromptTokens = totals.PromptTokens,
                CompletionTokens = totals.CompletionTokens,
                CostUsd = Usd(totals.CostMicros),
                LatencyP50Ms = Percentile(totals, 0.50),
                LatencyP95Ms = Percentile(totals, 0.95),
                TtftAverageMs = totals.TtftCount == 0 ? null : totals.TtftMsSum / totals.TtftCount,
                FallbackRate = counts.Replies == 0 ? 0 : Math.Round((double)counts.FallbackReplies / counts.Replies, 4),
                BlockedOutputs = counts.BlockedReplies,
                FailedReplies = counts.FailedReplies,
                MonthlyBudgetUsd = _App.Settings.Ai.MonthlyBudgetUsd > 0 ? _App.Settings.Ai.MonthlyBudgetUsd : null,
                MonthToDateCostUsd = await MonthToDateUsdAsync(null, token).ConfigureAwait(false)
            };
        }

        /// <summary>
        /// GET /api/v1/admin/ai/analytics/timeseries.
        /// </summary>
        /// <param name="days">Window in days (1 to 365).</param>
        /// <param name="metric">requests, errors, promptTokens, completionTokens, tokens, cost, latencyAvgMs, or latencyP95Ms.</param>
        /// <param name="granularity">Hour (windows up to 7 days) or Day.</param>
        /// <param name="assistantId">Assistant, or null.</param>
        /// <param name="endpointId">Endpoint, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Series.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public async Task<AiTimeseriesView> TimeseriesAsync(int days, string? metric, AiUsageGranularityEnum granularity, string? assistantId, string? endpointId, CancellationToken token)
        {
            int window = Math.Clamp(days, 1, 365);
            string name = String.IsNullOrWhiteSpace(metric) ? "requests" : metric.Trim();
            string[] metrics = new[] { "requests", "errors", "promptTokens", "completionTokens", "tokens", "cost", "latencyAvgMs", "latencyP95Ms" };
            if (!metrics.Contains(name)) throw ApiErrorException.Field("metric", "Must be one of: " + String.Join(", ", metrics) + ".");
            if (granularity == AiUsageGranularityEnum.Hour && window > 7) throw ApiErrorException.Field("granularity", "Hour is allowed for windows up to 7 days.");
            DateTime since = granularity == AiUsageGranularityEnum.Hour ? DateTime.UtcNow.AddHours(-window * 24) : DateTime.UtcNow.Date.AddDays(-(window - 1));
            List<AiUsageRollup> rows = await _App.Database.Ai.Usage.SeriesAsync(since, granularity, assistantId, endpointId, token).ConfigureAwait(false);
            AiTimeseriesView view = new AiTimeseriesView { Metric = name, Granularity = granularity };
            foreach (AiUsageRollup row in rows.Take(400))
            {
                double value;
                switch (name)
                {
                    case "errors": value = row.Errors; break;
                    case "promptTokens": value = row.PromptTokens; break;
                    case "completionTokens": value = row.CompletionTokens; break;
                    case "tokens": value = row.PromptTokens + row.CompletionTokens + row.ReasoningTokens; break;
                    case "cost": value = Usd(row.CostMicros); break;
                    case "latencyAvgMs": value = row.Requests == 0 ? 0 : (double)row.LatencyMsSum / row.Requests; break;
                    case "latencyP95Ms": value = Percentile(row, 0.95) ?? 0; break;
                    default: value = row.Requests; break;
                }
                view.Points.Add(new AiTimeseriesPoint { TimestampUtc = row.PeriodStartUtc, Value = Math.Round(value, 6) });
            }
            return view;
        }

        /// <summary>
        /// POST /api/v1/admin/ai/analytics/endpoints/enumerate (window filter windowDays, default 30).
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        public async Task<EnumerationResult<AiEndpointAnalyticsItem>> EndpointsAsync(EnumerationQuery? query, CancellationToken token)
        {
            EnumerationRequest request = _App.Enumeration.Prepare(query, AiCollections.EndpointAnalytics, true);
            DateTime since = WindowStart(request.Query);
            string? assistantId = request.Query.FilterString("assistantId");
            EnumerationResult<AiUsageRollup> page = await _App.Database.Ai.Usage.EnumerateEndpointTotalsAsync(request, since, assistantId, token).ConfigureAwait(false);
            List<AiUsageRollup> purposes = await _App.Database.Ai.Usage.PurposesAsync(page.Objects.Select(o => o.EndpointId), since, token).ConfigureAwait(false);
            List<AiEndpointAnalyticsItem> items = page.Objects.Select(row =>
            {
                string[] parts = row.Purpose.Split('|');
                AiEndpointAnalyticsItem item = new AiEndpointAnalyticsItem
                {
                    Endpoint = new AiEndpointRef { Id = row.EndpointId, Name = parts[0], ApiType = parts.Length > 1 ? parts[1] : String.Empty, Model = parts.Length > 2 ? parts[2] : String.Empty, Deleted = parts.Length > 3 && parts[3] == "deleted" },
                    Requests = row.Requests,
                    Errors = row.Errors,
                    ErrorRate = row.Requests == 0 ? 0 : Math.Round((double)row.Errors / row.Requests, 4),
                    LatencyP95Ms = Percentile(row, 0.95),
                    PromptTokens = row.PromptTokens,
                    CompletionTokens = row.CompletionTokens,
                    CostUsd = Usd(row.CostMicros)
                };
                foreach (AiUsageRollup p in purposes.Where(p => p.EndpointId == row.EndpointId))
                {
                    string key = Char.ToLowerInvariant(p.Purpose[0]) + p.Purpose.Substring(1);
                    item.Purposes[key] = (item.Purposes.TryGetValue(key, out long n) ? n : 0) + p.Requests;
                }
                return item;
            }).ToList();
            return page.With(items);
        }

        /// <summary>
        /// POST /api/v1/admin/ai/analytics/assistants/enumerate (window filter windowDays, default 30).
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        public async Task<EnumerationResult<AiAssistantAnalyticsItem>> AssistantsAsync(EnumerationQuery? query, CancellationToken token)
        {
            EnumerationRequest request = _App.Enumeration.Prepare(query, AiCollections.AssistantAnalytics, true);
            DateTime since = WindowStart(request.Query);
            EnumerationResult<AiUsageRollup> page = await _App.Database.Ai.Usage.EnumerateAssistantTotalsAsync(request, since, token).ConfigureAwait(false);
            Dictionary<string, AiActivityCounts> activity = await _App.Database.Ai.Usage.ActivityByAssistantAsync(page.Objects.Select(o => o.AssistantId), since, token).ConfigureAwait(false);
            List<Assistant> assistants = await _App.Database.Ai.Assistants.ReadManyAsync(page.Objects.Select(o => o.AssistantId), token).ConfigureAwait(false);
            List<AiAssistantAnalyticsItem> items = new List<AiAssistantAnalyticsItem>();
            foreach (AiUsageRollup row in page.Objects)
            {
                string[] parts = row.Purpose.Split('|');
                AiActivityCounts counts = activity.TryGetValue(row.AssistantId, out AiActivityCounts? c) ? c : new AiActivityCounts();
                long rated = counts.ThumbsUp + counts.ThumbsDown;
                Assistant? assistant = assistants.FirstOrDefault(a => a.Id == row.AssistantId);
                items.Add(new AiAssistantAnalyticsItem
                {
                    Assistant = new AiAssistantRef { Id = row.AssistantId, Name = parts[0], Deleted = parts.Length > 1 && parts[1] == "deleted" },
                    Conversations = counts.Conversations,
                    Messages = counts.Messages,
                    ThumbsUpRate = rated == 0 ? null : Math.Round((double)counts.ThumbsUp / rated, 4),
                    Requests = row.Requests,
                    ErrorRate = row.Requests == 0 ? 0 : Math.Round((double)row.Errors / row.Requests, 4),
                    LatencyP95Ms = Percentile(row, 0.95),
                    PromptTokens = row.PromptTokens,
                    CompletionTokens = row.CompletionTokens,
                    CostUsd = Usd(row.CostMicros),
                    MonthlyBudgetUsd = assistant?.MonthlyBudgetUsd,
                    MonthToDateCostUsd = await MonthToDateUsdAsync(row.AssistantId, token).ConfigureAwait(false)
                });
            }
            return page.With(items);
        }

        /// <summary>
        /// POST /api/v1/admin/ai/analytics/members/enumerate: cost by member (filters windowDays, assistantId, subjectType).
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        public async Task<EnumerationResult<AiMemberAnalyticsItem>> MembersAsync(EnumerationQuery? query, CancellationToken token)
        {
            EnumerationRequest request = _App.Enumeration.Prepare(query, AiCollections.MemberAnalytics, true);
            DateTime since = WindowStart(request.Query);
            AiEnumerationFilter filter = new AiEnumerationFilter
            {
                AssistantId = request.Query.FilterString("assistantId"),
                SubjectType = request.Query.FilterString("subjectType")
            };
            if (filter.SubjectType != null && filter.SubjectType != "User" && filter.SubjectType != "Visitor") throw ApiErrorException.Field("filters.subjectType", "Must be User or Visitor.");
            EnumerationResult<AiDailyUsage> page = await _App.Database.Ai.Usage.EnumerateMemberTotalsAsync(request, since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), filter, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> members = await _App.Summaries.BuildAsync(page.Objects.Where(o => o.SubjectType == "User").Select(o => (string?)o.SubjectId), token).ConfigureAwait(false);
            List<AiMemberAnalyticsItem> items = page.Objects.Select(o => new AiMemberAnalyticsItem
            {
                SubjectType = o.SubjectType,
                SubjectId = o.SubjectId,
                Member = o.SubjectType == "User" && members.TryGetValue(o.SubjectId, out UserSummary? s) ? s : null,
                Messages = o.Messages,
                PromptTokens = o.PromptTokens,
                CompletionTokens = o.CompletionTokens,
                CostUsd = Usd(o.CostMicros),
                LastActiveUtc = DateTime.SpecifyKind(DateTime.ParseExact(o.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Utc)
            }).ToList();
            return page.With(items);
        }

        /// <summary>
        /// US dollars from millionths of a dollar.
        /// </summary>
        /// <param name="micros">Micros.</param>
        /// <returns>Dollars rounded to 6 places.</returns>
        public static double Usd(long micros)
        {
            return Math.Round(micros / 1000000.0, 6);
        }

        /// <summary>
        /// A percentile latency from a rollup's fixed histogram (the bucket's upper bound; above 60 s uses the max).
        /// </summary>
        /// <param name="row">Rollup.</param>
        /// <param name="fraction">0 to 1.</param>
        /// <returns>Milliseconds, or null without requests.</returns>
        public static long? Percentile(AiUsageRollup row, double fraction)
        {
            ArgumentNullException.ThrowIfNull(row);
            long total = row.Buckets.Sum();
            if (total == 0) return null;
            long target = (long)Math.Ceiling(total * fraction);
            long seen = 0;
            for (int i = 0; i < row.Buckets.Count; i++)
            {
                seen += row.Buckets[i];
                if (seen >= target) return i < _BucketUpper.Length ? Math.Min(_BucketUpper[i], Math.Max(row.LatencyMsMax, 1)) : row.LatencyMsMax;
            }
            return row.LatencyMsMax;
        }

        #endregion

        #region Private-Methods

        private static DateTime MonthStart(DateTime now)
        {
            return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        private static long Micros(double usd)
        {
            return (long)Math.Round(usd * 1000000.0, MidpointRounding.AwayFromZero);
        }

        private static DateTime WindowStart(EnumerationQuery query)
        {
            int days = 30;
            string? raw = query.FilterString("windowDays");
            if (raw != null && Int32.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) days = Math.Clamp(parsed, 1, 365);
            return DateTime.UtcNow.Date.AddDays(-(days - 1));
        }

        private async Task CheckBudgetsAsync(string? assistantId, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            if (now < _NextBudgetCheckUtc) return;
            _NextBudgetCheckUtc = now.AddSeconds(30);
            DateTime month = MonthStart(now);
            string monthKey = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            int alertPercent = Math.Clamp(_App.Settings.Ai.BudgetAlertPercent, 1, 100);
            if (!String.IsNullOrEmpty(assistantId))
            {
                Assistant? assistant = await _App.Database.Ai.Assistants.ReadAsync(assistantId, token).ConfigureAwait(false);
                if (assistant?.MonthlyBudgetUsd != null && assistant.MonthlyBudgetUsd.Value > 0)
                {
                    long spent = await _App.Database.Ai.Usage.CostSinceAsync(month, assistantId, token).ConfigureAwait(false);
                    await AlertAsync("Assistant", assistantId, assistant.Name, monthKey, spent, Micros(assistant.MonthlyBudgetUsd.Value), alertPercent, now, token).ConfigureAwait(false);
                }
            }
            int property = _App.Settings.Ai.MonthlyBudgetUsd;
            if (property > 0)
            {
                long spent = await _App.Database.Ai.Usage.CostSinceAsync(month, null, token).ConfigureAwait(false);
                await AlertAsync("Property", _App.PropertyId, _App.Property.Name, monthKey, spent, Micros(property), alertPercent, now, token).ConfigureAwait(false);
            }
        }

        private async Task AlertAsync(string scope, string scopeId, string name, string month, long spent, long budget, int alertPercent, DateTime now, CancellationToken token)
        {
            if (budget <= 0) return;
            int level = spent >= budget ? 100 : spent * 100 >= budget * alertPercent ? alertPercent : 0;
            if (level == 0) return;
            if (!await _App.Database.Ai.Usage.TryRecordBudgetAlertAsync(scope, scopeId, month, level, now, token).ConfigureAwait(false)) return;
            TrellisKitTelemetry.Add(AiTelemetry.BudgetAlerts, 1, "scope", scope.ToLowerInvariant(), "level", level.ToString(CultureInfo.InvariantCulture));
            string to = String.IsNullOrWhiteSpace(_App.Settings.Ai.BudgetAlertEmail) ? _App.Property.Property.Contact.SupportEmail : _App.Settings.Ai.BudgetAlertEmail.Trim();
            if (String.IsNullOrWhiteSpace(to)) return;
            string what = scope == "Assistant" ? "The assistant " + name : _App.Property.Name;
            string text = what + " has used " + level + " percent of its monthly AI budget (" + Usd(spent).ToString("0.00", CultureInfo.InvariantCulture) + " of " +
                Usd(budget).ToString("0.00", CultureInfo.InvariantCulture) + " US dollars) for " + month + "." +
                (level >= 100 ? " Member chat stops when the hard stop is on." : String.Empty) + "\n\nOpen the admin console, then AI, then Analytics, to review usage.";
            try
            {
                await _App.Integrations.Email.SendAsync(new EmailMessage
                {
                    To = to,
                    Subject = _App.Property.Name + ": AI budget " + level + " percent used",
                    TextBody = text,
                    Category = "ai-budget"
                }, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _App.Logger.Warn("[AiUsageService] budget alert email failed: " + e.Message);
            }
        }

        private async Task RollupAsync(AiUsageRecord record, string apiType, bool fallback, string? guardKey)
        {
            string purpose = AiTelemetry.Label(record.Purpose.ToString());
            string outcome = AiTelemetry.Label(record.Outcome.ToString());
            TrellisKitTelemetry.Add(AiTelemetry.Requests, 1, "purpose", purpose, "api_type", apiType, "outcome", outcome, "fallback", fallback ? "true" : "false", "test", record.IsTest ? "true" : "false");
            TrellisKitTelemetry.Record(AiTelemetry.RequestDuration, record.LatencyMs / 1000.0, "purpose", purpose, "api_type", apiType, "outcome", outcome);
            if (record.TimeToFirstTokenMs != null) TrellisKitTelemetry.Record(AiTelemetry.TimeToFirstToken, record.TimeToFirstTokenMs.Value / 1000.0, "api_type", apiType);
            if (record.PromptTokens > 0) TrellisKitTelemetry.Add(AiTelemetry.Tokens, record.PromptTokens, "purpose", purpose, "api_type", apiType, "direction", "prompt");
            if (record.CompletionTokens > 0) TrellisKitTelemetry.Add(AiTelemetry.Tokens, record.CompletionTokens, "purpose", purpose, "api_type", apiType, "direction", "completion");
            if (record.ReasoningTokens > 0) TrellisKitTelemetry.Add(AiTelemetry.Tokens, record.ReasoningTokens, "purpose", purpose, "api_type", apiType, "direction", "reasoning");
            if (record.CostMicros > 0) TrellisKitTelemetry.Add(AiTelemetry.CostMicros, record.CostMicros, "purpose", purpose, "api_type", apiType);
            DetailMetrics(record, apiType, purpose);
            try
            {
                await _App.Database.Ai.Usage.ApplyAsync(record, guardKey, CancellationToken.None).ConfigureAwait(false);
                if (!record.IsTest && record.CostMicros > 0) await CheckBudgetsAsync(record.AssistantId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _App.Logger.Warn("[AiUsageService] usage rollup failed: " + e.Message);
            }
        }

        private static void DetailMetrics(AiUsageRecord record, string apiType, string purpose)
        {
            AiCallDetail d = record.Detail;
            if (d.CachedReadTokens != null && d.CachedReadTokens.Value > 0) TrellisKitTelemetry.Add(AiTelemetry.Tokens, d.CachedReadTokens.Value, "purpose", purpose, "api_type", apiType, "direction", "cache_read");
            if (d.CacheCreationTokens != null && d.CacheCreationTokens.Value > 0) TrellisKitTelemetry.Add(AiTelemetry.Tokens, d.CacheCreationTokens.Value, "purpose", purpose, "api_type", apiType, "direction", "cache_creation");
            if (d.TimeToLastTokenMs != null) TrellisKitTelemetry.Record(AiTelemetry.TimeToLastToken, d.TimeToLastTokenMs.Value / 1000.0, "api_type", apiType);
            if (d.StreamingMs != null) TrellisKitTelemetry.Record(AiTelemetry.StreamDuration, d.StreamingMs.Value / 1000.0, "api_type", apiType);
            if (d.TokensPerSecond != null) TrellisKitTelemetry.Record(AiTelemetry.TokensPerSecond, d.TokensPerSecond.Value, "api_type", apiType);
            if (d.RequestBytes != null) TrellisKitTelemetry.Record(AiTelemetry.RequestBytes, d.RequestBytes.Value, "api_type", apiType);
            if (d.ResponseBytes != null) TrellisKitTelemetry.Record(AiTelemetry.ResponseBytes, d.ResponseBytes.Value, "api_type", apiType);
        }

        private async Task<string?> LedgerAsync(AiUsageRecord record, string? endpointId, AiExecutorRuntimeEnum runtime, string? model, bool fallback, string? errorCode)
        {
            // docs/INTEGRATION.md: one ledger row per call, attributed to the ambient job and task role,
            // and the cost added to the scope so job items carry their model cost.
            AiCallScope? scope = AiCallScope.Current;
            scope?.AddCost(record.CostMicros);
            AiCallDetail detail = new AiCallDetail();
            record.Detail.CopyTo(detail);
            detail.TimeToFirstTokenMs ??= record.TimeToFirstTokenMs;
            detail.TotalTokens ??= record.PromptTokens + record.CompletionTokens + record.ReasoningTokens;
            if (detail.FinishReason != null && detail.FinishReason.Length > 100) detail.FinishReason = detail.FinishReason.Substring(0, 100);
            if (detail.ResponseId != null && detail.ResponseId.Length > 200) detail.ResponseId = detail.ResponseId.Substring(0, 200);
            if (detail.ProviderModel != null && detail.ProviderModel.Length > 200) detail.ProviderModel = detail.ProviderModel.Substring(0, 200);
            if (detail.ReasoningEffort != null && detail.ReasoningEffort.Length > 50) detail.ReasoningEffort = detail.ReasoningEffort.Substring(0, 50);
            string id = TrellisKit.Core.Helpers.IdGenerator.Generate("aic_");
            try
            {
                await _App.Database.Ai.Calls.InsertAsync(new AiCallRecord
                {
                    Id = id,
                    OccurredUtc = record.OccurredUtc,
                    Purpose = record.Purpose,
                    Role = scope?.Role,
                    AssistantId = record.AssistantId,
                    EndpointId = endpointId,
                    Runtime = runtime,
                    Model = model,
                    ExecutorPosition = scope?.ExecutorPosition,
                    EscalationReason = scope?.EscalationReason,
                    Outcome = record.Outcome,
                    ErrorCode = errorCode,
                    IsFallback = fallback,
                    IsTest = record.IsTest,
                    PromptTokens = record.PromptTokens,
                    CompletionTokens = record.CompletionTokens,
                    ReasoningTokens = record.ReasoningTokens,
                    LatencyMs = record.LatencyMs,
                    CostMicros = record.CostMicros,
                    JobKey = scope?.JobKey,
                    JobRunId = scope?.JobRunId,
                    JobItemId = scope?.JobItemId,
                    Detail = detail
                }, CancellationToken.None).ConfigureAwait(false);
                return id;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _App.Logger.Warn("[AiUsageService] call ledger write failed: " + e.Message);
                return null;
            }
        }

        #endregion
    }
}
