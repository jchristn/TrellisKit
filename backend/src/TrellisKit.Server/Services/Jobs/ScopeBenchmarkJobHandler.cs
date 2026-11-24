namespace TrellisKit.Server.Services.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Serialization;
    using TrellisKit.Server.Services.Ai.Benchmark;

    /// <summary>
    /// ai.scopeBenchmark: the live off-scope and prompt-injection benchmark (docs/INTEGRATION.md owner requirement R7,
    /// docs/BENCHMARKS.md). One item per case of the property's set (the engine set plus the property overlay) runs through the real scope layers with the run's
    /// prompt versions (a draft named by the run's parameters, otherwise the prompts in use); the item result is the
    /// graded case, and the run summary holds the pass rate per category, the prompt versions, and whether the run
    /// clears the publish gate. Manual by default. A dry run measures the deterministic detector only and makes no
    /// model calls. Cases that cannot be measured fail their item and make the run incomplete.
    /// </summary>
    public sealed class ScopeBenchmarkJobHandler : IJobHandler, IJobRunSummarizer
    {
        #region Public-Members

        /// <inheritdoc />
        public string Key
        {
            get { return ScopeBenchmarkService.JobKey; }
        }

        /// <inheritdoc />
        public JobHandlerDefaults Defaults { get; } = new JobHandlerDefaults
        {
            Name = "Scope benchmark",
            Description = "Runs the off-scope and prompt-injection benchmark through the classifier, the guarded reply, and the post-call check, for a prompt draft or the prompts in use. Publishing a prompt draft needs a passing run.",
            Enabled = false,
            IntervalType = JobIntervalTypeEnum.Manual,
            MaxConcurrency = 4,
            MaxAttempts = 2,
            BudgetUsdPerRun = 10,
            SupportsDryRun = true
        };

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<List<JobItemPlan>> PlanAsync(JobRunContext context, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            ScopeBenchmarkSet set = context.Services.Ai.Benchmarks.Set();
            ScopeBenchmarkParameters parameters = ScopeBenchmarkService.ParseParameters(context.Run.Parameters);
            List<ScopePromptVersion> prompts = await context.Services.Ai.Benchmarks.CandidateAsync(parameters, token).ConfigureAwait(false);
            return set.Cases.Select(c => new JobItemPlan(c.Id, ApiJson.Serialize(new ScopeBenchmarkItem { CaseId = c.Id, Prompts = prompts }))).ToList();
        }

        /// <inheritdoc />
        public async Task<JobItemOutcome> ExecuteAsync(JobItemContext context, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            ScopeBenchmarkItem? item = ParseItem(context.Item.Payload);
            ScopeBenchmarkCase? benchmarkCase = context.Services.Ai.Benchmarks.Set().Find(item?.CaseId ?? context.Item.TargetKey);
            if (item == null || benchmarkCase == null) return JobItemOutcome.Fail("Unknown benchmark case " + context.Item.TargetKey + ".", false);
            ScopeBenchmarkParameters parameters = ScopeBenchmarkService.ParseParameters(context.Run.Parameters);
            try
            {
                ScopeCaseResult result = await context.Services.Ai.Benchmarks.RunCaseAsync(benchmarkCase, parameters, item.Prompts, context.IsDryRun, token).ConfigureAwait(false);
                return JobItemOutcome.Ok(ApiJson.Serialize(result));
            }
            catch (ScopeBenchmarkException e)
            {
                return JobItemOutcome.Fail(e.Message, e.Retryable);
            }
        }

        /// <inheritdoc />
        public async Task<string?> SummarizeAsync(JobRunContext context, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            Dictionary<string, ScopeCaseResult> results = new Dictionary<string, ScopeCaseResult>(StringComparer.Ordinal);
            List<ScopePromptVersion>? prompts = null;
            string? continuation = null;
            for (int page = 0; page < 1000; page++)
            {
                EnumerationRequest request = context.Services.Enumeration.Prepare(new EnumerationQuery { MaxResults = 100, ContinuationToken = continuation }, JobCollections.AdminItems, true);
                EnumerationResult<JobItem> items = await context.Services.Database.Jobs.EnumerateItemsAsync(request, context.Run.Id, null, new List<JobItemStatusEnum>(), token).ConfigureAwait(false);
                foreach (JobItem i in items.Objects)
                {
                    if (prompts == null) prompts = ParseItem(i.Payload)?.Prompts;
                    if (i.Status != JobItemStatusEnum.Succeeded || String.IsNullOrWhiteSpace(i.Result)) continue;
                    ScopeCaseResult? result = ParseResult(i.Result);
                    if (result != null) results[result.CaseId] = result;
                }
                if (items.EndOfResults || String.IsNullOrEmpty(items.ContinuationToken)) break;
                continuation = items.ContinuationToken;
            }

            ScopeBenchmarkSummary summary = ScopeBenchmarkService.Summarize(context.Services.Ai.Benchmarks.Set(), results, ScopeBenchmarkService.ParseParameters(context.Run.Parameters), prompts ?? new List<ScopePromptVersion>(), context.IsDryRun);
            return ApiJson.Serialize(summary);
        }

        #endregion

        #region Private-Methods

        private static ScopeBenchmarkItem? ParseItem(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<ScopeBenchmarkItem>(json, ApiJson.Api);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static ScopeCaseResult? ParseResult(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<ScopeCaseResult>(json, ApiJson.Api);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}
