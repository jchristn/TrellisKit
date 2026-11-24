namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Server.Services.Jobs;

    /// <summary>
    /// A configurable job handler for the job engine suite. Targets starting ok succeed, fail fail with a retryable
    /// error, fatal fail without retry, and throw throw; Behavior overrides that.
    /// </summary>
    internal sealed class TestJobHandler : IJobHandler, IJobRunSummarizer
    {
        public string Key { get; }

        public JobHandlerDefaults Defaults { get; }

        public List<string> Targets { get; set; } = new List<string>();

        public long CostMicrosPerItem { get; set; } = 0;

        public int DelayMs { get; set; } = 0;

        public Func<JobRunContext, CancellationToken, Task<List<JobItemPlan>>>? PlanBehavior { get; set; } = null;

        public Func<JobRunContext, CancellationToken, Task<string?>>? SummaryBehavior { get; set; } = null;

        public Task<string?> SummarizeAsync(JobRunContext context, CancellationToken token)
        {
            return SummaryBehavior == null ? Task.FromResult<string?>(null) : SummaryBehavior(context, token);
        }

        public Func<JobItemContext, JobItemOutcome?>? Behavior { get; set; } = null;

        public ConcurrentBag<bool> DryRunsSeen { get; } = new ConcurrentBag<bool>();

        public ConcurrentQueue<string> Executed { get; } = new ConcurrentQueue<string>();

        public int MaxObservedConcurrency
        {
            get { return _MaxObserved; }
        }

        private int _Current = 0;
        private int _MaxObserved = 0;

        internal TestJobHandler(string? key = null, JobHandlerDefaults? defaults = null)
        {
            Key = key ?? "test.j" + Guid.NewGuid().ToString("N").Substring(0, 10);
            Defaults = defaults ?? new JobHandlerDefaults { Name = "Test job " + Key, Description = "Job engine suite handler.", MaxConcurrency = 4, MaxAttempts = 2 };
        }

        public Task<List<JobItemPlan>> PlanAsync(JobRunContext context, CancellationToken token)
        {
            if (PlanBehavior != null) return PlanBehavior(context, token);
            return Task.FromResult(Targets.Select(t => new JobItemPlan(t, "{\"target\":\"" + t + "\"}")).ToList());
        }

        public async Task<JobItemOutcome> ExecuteAsync(JobItemContext context, CancellationToken token)
        {
            int now = Interlocked.Increment(ref _Current);
            int seen;
            do
            {
                seen = _MaxObserved;
                if (now <= seen) break;
            }
            while (Interlocked.CompareExchange(ref _MaxObserved, now, seen) != seen);
            try
            {
                DryRunsSeen.Add(context.IsDryRun);
                Executed.Enqueue(context.Item.TargetKey);
                if (DelayMs > 0) await Task.Delay(DelayMs, token).ConfigureAwait(false);
                JobItemOutcome? custom = Behavior?.Invoke(context);
                if (custom != null) return custom;
                string target = context.Item.TargetKey;
                if (target.StartsWith("ok", StringComparison.Ordinal)) return JobItemOutcome.Ok("{\"done\":\"" + target + "\"}", CostMicrosPerItem);
                if (target.StartsWith("fatal", StringComparison.Ordinal)) return JobItemOutcome.Fail("Bad input.", false, CostMicrosPerItem);
                if (target.StartsWith("throw", StringComparison.Ordinal)) throw new InvalidOperationException("Handler exploded.");
                return JobItemOutcome.Fail("Temporary failure.", true, CostMicrosPerItem);
            }
            finally
            {
                Interlocked.Decrement(ref _Current);
            }
        }
    }
}
