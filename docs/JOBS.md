# Job engine

The job engine runs property-scoped background work, including content review, AI scope benchmarks, call-ledger retention, and handlers registered by a property. The admin console is at `/admin/jobs`; the routes are in [REST_API.md](../REST_API.md) under "Admin: jobs".

Licensed under the MIT License. See [LICENSE](../LICENSE).

## Model

| Concept | Table | What it is |
|---------|-------|------------|
| Definition | `jobdefinitions` | One per registered handler: schedule, enabled flag, per-run concurrency, attempts per item, run budget, whether a CLI agent harness is required. Created from the handler's defaults; administrators change it afterward. |
| Run | `jobruns` | One execution: trigger (`Schedule`, `Manual`, `DryRun`), status, item counts, cost. |
| Item | `jobitems` | One unit of work inside a run (a document, a source, a catalog entry), unique by target key within the run. Retried with exponential backoff and jitter; `Dead` after the last attempt. |

A pass (every `jobs.tickSeconds`, on every instance) does this:

1. Fires due schedules into `Queued` runs. A run is recorded as `Skipped` when the previous run is still active or the monthly cap is reached.
2. Returns items whose claim expired (their worker stopped) to `Pending`, or to `Dead` when no attempt remains.
3. Claims one queued run at a time with a fenced five-minute planning lease, then calls the handler's `PlanAsync`. The run stays `Queued` until all items and the transition to `Running` commit together. Other workers cannot execute or finish it while planning. An expired claim can be taken over; a late planner cannot overwrite a newer claim or cancellation.
4. Claims due items of each running run up to its `maxConcurrency` and calls `ExecuteAsync`.
5. Finishes runs with no open items: `Succeeded`, `PartiallySucceeded`, or `Failed`.

Every claim uses a locking CTE with `FOR UPDATE SKIP LOCKED`. Item claims first lock the run row, so any number of instances share the work without exceeding a run's concurrency. Do not rewrite a claim as `UPDATE ... WHERE id IN (SELECT ... LIMIT n FOR UPDATE SKIP LOCKED)`: PostgreSQL may evaluate that subquery more than once and lock more than `n` rows.

Item completion is fenced by both worker identity and attempt number: an expired attempt cannot overwrite a later claim, including one acquired by the same worker. Each run receives a fresh item lease deadline when its items are claimed.

Cancellation atomically marks the run `Cancelled` and skips its pending items under the run lock. New item claims require a running run. Already executing items can finish, but a late summary or worker update cannot restore a cancelled run to a running or successful state.

## Budgets

- **Monthly cap**: `jobs.monthlyBudgetUsd` (default $2,000) across every job of a property per UTC calendar month. At the cap, run now answers `409 BudgetExceeded`, scheduled runs are skipped, and running runs skip their pending items.
- **Run budget**: `budgetUsdPerRun` on a definition. Once the run's cost reaches it, the remaining items are skipped. Both budgets are checked before claiming, with the monthly total refreshed for every run in the pass, so the last batch of items may overshoot slightly.
- Model calls made during an attempt (through task roles or the AI services, including CLI agent harness calls with the cost they report) are added to the item's cost automatically, and the call ledger names the job, run, and item. Handlers report only other paid costs (for example a paid search API) in `JobItemOutcome.CostMicros` (micro-dollars). Do not report model costs again.

## Parameters and summaries

A run can carry handler-specific **parameters** (JSON, `jobruns.parameters`), set when a feature queues it through `JobService.QueueAsync` (for example the scope benchmark of one prompt draft). `POST /api/v1/admin/jobs/{key}/run` queues runs without parameters. A handler that also implements `IJobRunSummarizer` writes a **summary** (JSON, at most 65,536 characters, `jobruns.summary`) when the run's last item finishes. Both appear on `JobRun` as `parameters` and `summary`. A summarizer that throws leaves the run without a summary and records the error; the run's status is not changed. Built-in example: `ai.scopeBenchmark` ([BENCHMARKS.md](BENCHMARKS.md)).

## Writing a handler

```csharp
public sealed class DocumentRefreshHandler : IJobHandler
{
    public string Key { get { return "documents.refresh"; } }

    public JobHandlerDefaults Defaults { get; } = new JobHandlerDefaults
    {
        Name = "Refresh reference documents",
        Description = "Refreshes each registered reference source.",
        IntervalType = JobIntervalTypeEnum.DaysOfWeek,
        DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Monday },
        TimeOfDayMinutesUtc = 9 * 60,
        MaxConcurrency = 4,
        MaxAttempts = 3,
        BudgetUsdPerRun = 50,
        RequiresHarness = true
    };

    public Task<List<JobItemPlan>> PlanAsync(JobRunContext context, CancellationToken token) { /* one item per source */ }

    public Task<JobItemOutcome> ExecuteAsync(JobItemContext context, CancellationToken token) { /* refresh one source */ }
}
```

Register it on the property's job service at startup: `services.Jobs.Registry.Register(new DocumentRefreshHandler());`.

Rules:

- Keys are dotted lower camel case (`area.name`) and never change, because a definition and its history are keyed by them.
- `PlanAsync` should only describe work and must honor cancellation; it has a five-minute timeout. Planning can repeat after a worker stops, and only the current claim can publish its items.
- `ExecuteAsync` must be idempotent per item, because an item is retried after a worker stops mid-attempt. Honor `context.IsDryRun` by writing nothing lasting, or set `SupportsDryRun = false`.
- Return `JobItemOutcome.Fail(message, retryable: false)` for input that will never succeed. Exceptions count as retryable failures.
- Respect the cancellation token. Attempts are cut off before the item claim (`jobs.itemLockMinutes`) expires.
- Handlers that need a CLI agent harness set `RequiresHarness = true`. Their items are claimed only by instances with `jobs.runHarnesses` (worker instances whose image carries the CLIs), never in request paths.
- Agentic work with structured results goes through `services.Ai.Roles.RunAgentAsync(role, request, token)` with typed job tools ([HARNESSES.md](HARNESSES.md)). A harness executor reaches the tools through a job-scoped MCP server and an API executor through the engine's tool loop, so one handler works with either. Harness executors run only inside job items on `jobs.runHarnesses` instances, in `Restricted` or `ReadOnly` mode, in a throwaway directory, within `jobs.harnessTimeoutSeconds`; elsewhere they are passed over and the next executor runs. Tool handlers are the only way an agent's output reaches the database, so they must validate and store idempotently.
- Model calls go through a task role (`services.Ai.Roles.PromptAsync("Extraction", ...)`), never a specific endpoint, so administrators choose the models, the scope guardrail applies, and the call is accounted. A failed role call with `Retryable` set (the Defer policy, or a busy hierarchy) should return a retryable failure so the item backs off.
