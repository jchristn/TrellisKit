namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services.Jobs;

    /// <summary>
    /// The job engine (docs/INTEGRATION.md): run now and planning, retries with backoff, the dead-letter
    /// view and retry, schedules and skipped runs, per-run concurrency, the run budget and monthly cap, dry runs,
    /// cancellation, expired claims, history pruning, schedule arithmetic, and administrator-only access.
    /// </summary>
    public static class JobEngineSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string Id = "JobEngine";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(Id, "Job engine (" + TestEnvironment.Provider + ")", new[]
            {
                SuiteCases.Http(Id, "Runs.LifecycleRetryDeadLetter", "Run now plans items; successes finish, retryable failures back off and dead-letter after the last attempt, non-retryable failures fail, and a retried dead item reopens the run", LifecycleAsync),
                SuiteCases.Http(Id, "Planning.ConcurrentAndCancelled", "Another worker cannot finish a run while its planner is blocked, and cancellation cannot be overwritten by a late plan", PlanningAsync),
                SuiteCases.Http(Id, "Planning.ExpiredFencing", "A stopped planner is reclaimed after its lease, and its stale result cannot publish items or overwrite the winner", PlanningLeaseAsync),
                SuiteCases.Http(Id, "Runs.CancelDuringSummary", "Cancellation during a slow summary cannot be overwritten by a late worker or followed by a stale item claim", CancelSummaryAsync),
                SuiteCases.Http(Id, "Items.ReclaimedAttemptFencing", "A late attempt cannot complete an item reclaimed by the same worker id", AttemptFenceAsync),
                SuiteCases.Http(Id, "Items.LeaseClockPerRun", "Each run receives a fresh item claim deadline after a preceding run takes time", LeaseClockAsync),
                SuiteCases.Http(Id, "Budgets.RecheckedBetweenRuns", "A run that consumes the monthly budget prevents the next run in the same pass from executing", BudgetBetweenRunsAsync),
                SuiteCases.Http(Id, "Schedules.FireAndSkip", "An enabled schedule queues a run when due, a due schedule with an active run records a skipped run, and invalid schedules are 400", ScheduleAsync),
                SuiteCases.Http(Id, "Concurrency.PerRunLimit", "No more items of a run execute at once than its maxConcurrency", ConcurrencyAsync),
                SuiteCases.Http(Id, "Budgets.RunAndMonthly", "The run budget skips remaining items, and a spent monthly cap refuses run now with 409 BudgetExceeded and skips scheduled runs", BudgetAsync),
                SuiteCases.Http(Id, "Runs.DryRunAndCancel", "Dry runs reach the handler as dry runs, handlers without dry run support refuse them, and cancel skips pending items", DryRunCancelAsync),
                SuiteCases.Http(Id, "Items.ExpiredClaimRecovers", "An item whose worker stopped returns to pending after its claim expires and then completes", ExpiredClaimAsync),
                SuiteCases.Http(Id, "Prune.DeletesOldHistory", "The built-in jobs.prune job deletes finished runs older than jobs.retentionDays and keeps recent ones", PruneAsync),
                SuiteCases.Http(Id, "Schedule.Arithmetic", "Next occurrences for minutes, hours, days at a time of day, days of week, and manual", ArithmeticAsync),
                SuiteCases.Http(Id, "Definitions.FullEnumeration", "More than 500 definitions remain reachable through bounded keyset pages, exact counts, metadata and search", DefinitionsAsync),
                SuiteCases.Http(Id, "Admin.Access", "Members are refused, the list carries the built-in job, unknown keys are 404, and the overview reports the default $2,000 cap", AccessAsync)
            });
        }

        #endregion

        #region Private-Methods

        private const string Jobs = "/api/v1/admin/jobs";

        private static async Task DefinitionsAsync(HttpFixture f, CancellationToken ct)
        {
            DateTime created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 507; i++)
            {
                string key = "paging." + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
                await f.Database.Jobs.InsertDefinitionIfMissingAsync(new JobDefinition { Id = "jbd_" + key, Key = key, Name = key, CreatedUtc = created, UpdatedUtc = created }, ct).ConfigureAwait(false);
            }
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            string? continuation = null;
            do
            {
                JsonElement page = await Api.ExpectAsync(f, HttpMethod.Post, Jobs + "/enumerate", HttpFixture.AdminToken,
                    new { maxResults = 73, search = "paging.", continuationToken = continuation }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                Check.Equal(507L, page.GetProperty("totalRecords").GetInt64(), "Exact full collection count");
                Check.True(page.GetProperty("objects").GetArrayLength() <= 73, "Page bounded");
                foreach (JsonElement item in page.GetProperty("objects").EnumerateArray()) Check.True(ids.Add(Api.Str(item, "id")), "Each definition appears once");
                Check.Equal(507L - ids.Count, page.GetProperty("recordsRemaining").GetInt64(), "Exact remaining count");
                continuation = page.GetProperty("continuationToken").GetString();
                Check.Equal(continuation == null, page.GetProperty("endOfResults").GetBoolean(), "Cursor ends exactly at last page");
            } while (continuation != null);
            Check.Equal(507, ids.Count, "Definitions beyond old 500 cap reachable");
            await Api.ExpectAsync(f, HttpMethod.Put, "/api/v1/metadata/JobDefinition/jbd_paging.0506", HttpFixture.AdminToken,
                new { labels = new[] { "tail" }, tags = new { lane = "review" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement filtered = await Api.ExpectAsync(f, HttpMethod.Post, Jobs + "/enumerate", HttpFixture.AdminToken,
                new { labels = new[] { "TAIL" }, tags = new { lane = "review" } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, filtered.GetProperty("objects").GetArrayLength(), "SQL metadata filtering");
            Check.Equal("tail", filtered.GetProperty("objects")[0].GetProperty("labels")[0].GetString() ?? "", "Metadata attached");
            JsonElement get = await Api.ExpectAsync(f, HttpMethod.Get, Jobs + "?search=paging.&maxResults=3&skip=500", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(3, get.GetProperty("objects").GetArrayLength(), "GET has same bounded semantics");
            Check.Equal(507L, get.GetProperty("totalRecords").GetInt64(), "GET exact totals");
        }

        private static TestJobHandler Register(HttpFixture f, params string[] targets)
        {
            TestJobHandler handler = new TestJobHandler { Targets = targets.ToList() };
            f.Services.Jobs.Registry.Register(handler);
            return handler;
        }

        private static async Task<string> RunNowAsync(HttpFixture f, string key, CancellationToken ct, bool dryRun = false)
        {
            JsonElement run = await Api.ExpectAsync(f, HttpMethod.Post, Jobs + "/" + key + "/run", HttpFixture.AdminToken, new { dryRun }, HttpStatusCode.Created, ct).ConfigureAwait(false);
            Check.Equal(dryRun ? "DryRun" : "Manual", Api.Str(run, "trigger"), "Trigger");
            Check.Equal("Queued", Api.Str(run, "status"), "Queued");
            return Api.Str(run, "id");
        }

        private static async Task<JobRun> RunAsync(HttpFixture f, string id, CancellationToken ct)
        {
            return (await f.Services.Database.Jobs.ReadRunAsync(id, ct).ConfigureAwait(false))!;
        }

        private static async Task LifecycleAsync(HttpFixture f, CancellationToken ct)
        {
            DateTime now = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
            f.Services.Jobs.Clock = () => now;
            TestJobHandler handler = Register(f, "ok-1", "ok-2", "fail-1", "fatal-1", "ok-2");
            string runId = await RunNowAsync(f, handler.Key, ct).ConfigureAwait(false);
            await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
            JobRun run = await RunAsync(f, runId, ct).ConfigureAwait(false);
            Check.Equal(JobRunStatusEnum.Running, run.Status, "Running while an item waits for its retry");
            Check.Equal(4, run.ItemsTotal, "Duplicate targets are planned once");
            JobItemCounts counts = await f.Services.Database.Jobs.CountItemsAsync(runId, ct).ConfigureAwait(false);
            Check.Equal(2, counts.Succeeded, "Two successes");
            Check.Equal(1, counts.Failed, "The non-retryable failure failed");
            Check.Equal(1, counts.Open, "The retryable failure waits");

            await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
            Check.Equal(4, handler.Executed.Count, "Nothing runs before the backoff elapses");
            now = now.AddHours(2);
            await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
            run = await RunAsync(f, runId, ct).ConfigureAwait(false);
            Check.Equal(JobRunStatusEnum.PartiallySucceeded, run.Status, "Finished with failures");
            Check.Equal(1, run.ItemsDead, "Dead-lettered after maxAttempts (2)");

            JsonElement dead = await Api.ExpectAsync(f, HttpMethod.Post, Jobs + "/items/enumerate", HttpFixture.AdminToken, new { filters = new { status = new[] { "Dead" }, runId } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> deadItems = dead.GetProperty("objects").EnumerateArray().ToList();
            Check.Equal(1, deadItems.Count, "One dead item");
            Check.Equal("fail-1", Api.Str(deadItems[0], "targetKey"), "The failing target");
            Check.Equal(2, deadItems[0].GetProperty("attempts").GetInt32(), "Two attempts");
            JsonElement runItems = await Api.ExpectAsync(f, HttpMethod.Post, Jobs + "/runs/" + runId + "/items/enumerate", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(4, runItems.GetProperty("objects").GetArrayLength(), "Every item of the run");

            handler.Behavior = c => c.Item.TargetKey == "fail-1" ? JobItemOutcome.Ok() : null;
            JsonElement retried = await Api.ExpectAsync(f, HttpMethod.Post, Jobs + "/items/" + Api.Str(deadItems[0], "id") + "/retry", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Pending", Api.Str(retried, "status"), "Back to pending");
            Check.Equal(JobRunStatusEnum.Running, (await RunAsync(f, runId, ct).ConfigureAwait(false)).Status, "The run reopened");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Jobs + "/items/" + Api.Str(deadItems[0], "id") + "/retry", HttpFixture.AdminToken, new { }, HttpStatusCode.Conflict, "NotRetryable", ct).ConfigureAwait(false);
            await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
            JsonElement view = await Api.ExpectAsync(f, HttpMethod.Get, Jobs + "/runs/" + runId, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(3, view.GetProperty("itemsSucceeded").GetInt32(), "The retried item succeeded");
            Check.Equal(0, view.GetProperty("itemsDead").GetInt32(), "No dead items");
            Check.Equal("PartiallySucceeded", Api.Str(view, "status"), "The fatal item still failed");
            JsonElement runs = await Api.ExpectAsync(f, HttpMethod.Post, Jobs + "/runs/enumerate", HttpFixture.AdminToken, new { filters = new { key = handler.Key } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(1, runs.GetProperty("objects").GetArrayLength(), "Runs filter by key");
            Check.True(await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM auditrecords WHERE action IN ('job.run', 'job.item.retry');", ct).ConfigureAwait(false) >= 2, "Audited");
        }

        private static async Task ScheduleAsync(HttpFixture f, CancellationToken ct)
        {
            DateTime now = new DateTime(2026, 11, 21, 12, 0, 30, DateTimeKind.Utc);
            f.Services.Jobs.Clock = () => now;
            TestJobHandler handler = Register(f, "ok-1");
            string path = Jobs + "/" + handler.Key;
            // The built-in content.review job runs every 5 minutes; keep it out of this test's tick counts.
            await Api.ExpectAsync(f, HttpMethod.Put, Jobs + "/content.review", HttpFixture.AdminToken, new { enabled = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, path, HttpFixture.AdminToken, new { enabled = true, intervalType = "EveryNMinutes", intervalValue = 2 }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Put, path, HttpFixture.AdminToken, new { intervalType = "DaysOfWeek", daysOfWeek = new[] { "Funday" } }, HttpStatusCode.BadRequest, "ValidationFailed", ct).ConfigureAwait(false);
            JsonElement saved = await Api.ExpectAsync(f, HttpMethod.Put, path, HttpFixture.AdminToken, new { enabled = true, intervalType = "EveryNMinutes", intervalValue = 5 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(new DateTime(2026, 11, 21, 12, 5, 0, DateTimeKind.Utc), saved.GetProperty("nextRunUtc").GetDateTime().ToUniversalTime(), "Next run in 5 minutes on the minute");
            Check.Equal(0, await f.Services.Jobs.TickAsync(ct).ConfigureAwait(false), "Not due yet");
            now = now.AddMinutes(6);
            Check.Equal(1, await f.Services.Jobs.TickAsync(ct).ConfigureAwait(false), "Due: one run queued");
            now = now.AddMinutes(6);
            Check.Equal(0, await f.Services.Jobs.TickAsync(ct).ConfigureAwait(false), "The queued run is still active");
            JsonElement runs = await Api.ExpectAsync(f, HttpMethod.Post, Jobs + "/runs/enumerate", HttpFixture.AdminToken, new { filters = new { key = handler.Key } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            List<JsonElement> list = runs.GetProperty("objects").EnumerateArray().ToList();
            Check.Equal(2, list.Count, "A queued run and a skipped run");
            Check.Equal("Skipped", Api.Str(list[0], "status"), "Newest first: skipped");
            Check.True(Api.Str(list[0], "skippedReason").Contains("still active", StringComparison.Ordinal), "Reason recorded");
            Check.Equal("Schedule", Api.Str(list[1], "trigger"), "Scheduled trigger");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, path + "/run", HttpFixture.AdminToken, new { }, HttpStatusCode.Conflict, "RunActive", ct).ConfigureAwait(false);
            await Api.ExpectAsync(f, HttpMethod.Put, path, HttpFixture.AdminToken, new { enabled = false }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement off = await Api.ExpectAsync(f, HttpMethod.Get, path, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(off.GetProperty("nextRunUtc").ValueKind == JsonValueKind.Null, "Disabled schedules have no next run");
        }

        private static async Task ConcurrencyAsync(HttpFixture f, CancellationToken ct)
        {
            TestJobHandler handler = Register(f, "ok-1", "ok-2", "ok-3", "ok-4", "ok-5", "ok-6");
            handler.DelayMs = 60;
            await Api.ExpectAsync(f, HttpMethod.Put, Jobs + "/" + handler.Key, HttpFixture.AdminToken, new { maxConcurrency = 2 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string runId = await RunNowAsync(f, handler.Key, ct).ConfigureAwait(false);
            int passes = 0;
            while ((await RunAsync(f, runId, ct).ConfigureAwait(false)).Status != JobRunStatusEnum.Succeeded && passes < 10)
            {
                await Task.WhenAll(f.Services.Jobs.ProcessAsync(ct), f.Services.Jobs.ProcessAsync(ct)).ConfigureAwait(false);
                passes++;
            }
            Check.Equal(JobRunStatusEnum.Succeeded, (await RunAsync(f, runId, ct).ConfigureAwait(false)).Status, "Finished");
            Check.Equal(6, handler.Executed.Count, "Each item ran once even with two passes at a time");
            Check.True(handler.MaxObservedConcurrency <= 2, "Two concurrent passes together respect the limit of 2 (observed " + handler.MaxObservedConcurrency + ")");
            Check.True(passes >= 2, "Six items at two at a time need several passes");
        }

        private static async Task BudgetAsync(HttpFixture f, CancellationToken ct)
        {
            TestJobHandler handler = Register(f, "ok-1", "ok-2", "ok-3", "ok-4", "ok-5");
            handler.CostMicrosPerItem = 5;
            await Api.ExpectAsync(f, HttpMethod.Put, Jobs + "/" + handler.Key, HttpFixture.AdminToken, new { maxConcurrency = 1, budgetUsdPerRun = 0.00001 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            string runId = await RunNowAsync(f, handler.Key, ct).ConfigureAwait(false);
            for (int i = 0; i < 6; i++) await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
            JobRun run = await RunAsync(f, runId, ct).ConfigureAwait(false);
            Check.Equal(JobRunStatusEnum.PartiallySucceeded, run.Status, "Stopped by the run budget");
            Check.Equal(2, run.ItemsSucceeded, "Two items fit the $0.00001 budget at 5 micro-dollars each, one at a time");
            Check.Equal(3, run.ItemsSkipped, "The rest were skipped");
            Check.Equal(10L, run.CostMicros, "Cost recorded");
            JsonElement overview = await Api.ExpectAsync(f, HttpMethod.Get, Jobs + "/overview", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(Math.Abs(overview.GetProperty("monthCostUsd").GetDouble() - 0.00001) < 1e-9, "The month's cost includes the run");

            f.Settings.Jobs.MonthlyBudgetUsd = 0.00001;
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Jobs + "/" + handler.Key + "/run", HttpFixture.AdminToken, new { }, HttpStatusCode.Conflict, "BudgetExceeded", ct).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            f.Services.Jobs.Clock = () => now;
            await Api.ExpectAsync(f, HttpMethod.Put, Jobs + "/" + handler.Key, HttpFixture.AdminToken, new { enabled = true, intervalType = "EveryNMinutes", intervalValue = 5 }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            now = now.AddMinutes(6);
            Check.Equal(0, await f.Services.Jobs.TickAsync(ct).ConfigureAwait(false), "No run is queued over the cap");
            JsonElement runs = await Api.ExpectAsync(f, HttpMethod.Post, Jobs + "/runs/enumerate", HttpFixture.AdminToken, new { filters = new { key = handler.Key, status = new[] { "Skipped" } } }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.True(runs.GetProperty("objects").EnumerateArray().Any(r => Api.Str(r, "skippedReason").Contains("monthly job budget", StringComparison.Ordinal)), "A skipped run names the monthly budget");
        }

        private static async Task DryRunCancelAsync(HttpFixture f, CancellationToken ct)
        {
            TestJobHandler handler = Register(f, "ok-1");
            string dry = await RunNowAsync(f, handler.Key, ct, dryRun: true).ConfigureAwait(false);
            await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
            Check.Equal(JobRunStatusEnum.Succeeded, (await RunAsync(f, dry, ct).ConfigureAwait(false)).Status, "Dry run finished");
            Check.True(handler.DryRunsSeen.All(d => d) && handler.DryRunsSeen.Count == 1, "The handler saw a dry run");

            TestJobHandler noDry = new TestJobHandler(defaults: new JobHandlerDefaults { Name = "No dry run", SupportsDryRun = false }) { Targets = new List<string> { "ok-1", "ok-2", "ok-3" } };
            f.Services.Jobs.Registry.Register(noDry);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Jobs + "/" + noDry.Key + "/run", HttpFixture.AdminToken, new { dryRun = true }, HttpStatusCode.BadRequest, "DryRunNotSupported", ct).ConfigureAwait(false);
            string runId = await RunNowAsync(f, noDry.Key, ct).ConfigureAwait(false);
            JsonElement cancelled = await Api.ExpectAsync(f, HttpMethod.Post, Jobs + "/runs/" + runId + "/cancel", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal("Cancelled", Api.Str(cancelled, "status"), "Cancelled");
            await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
            Check.Equal(0, noDry.Executed.Count, "A cancelled queued run never executes");
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Jobs + "/runs/" + runId + "/cancel", HttpFixture.AdminToken, new { }, HttpStatusCode.Conflict, "RunFinished", ct).ConfigureAwait(false);
        }

        private static async Task PlanningAsync(HttpFixture f, CancellationToken ct)
        {
            TestJobHandler handler = Register(f, "ok-planned");
            foreach (bool cancel in new[] { false, true })
            {
                TaskCompletionSource started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                handler.PlanBehavior = async (context, token) =>
                {
                    started.TrySetResult();
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    return new List<JobItemPlan> { new JobItemPlan("ok-planned", null) };
                };
                string id = await RunNowAsync(f, handler.Key, ct).ConfigureAwait(false);
                Task<int> first = f.Services.Jobs.ProcessAsync(ct);
                try
                {
                    await started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
                    Check.Equal(JobRunStatusEnum.Queued, (await RunAsync(f, id, ct).ConfigureAwait(false)).Status, "Planning stays non-executable");
                    if (cancel) await Api.ExpectAsync(f, HttpMethod.Post, Jobs + "/runs/" + id + "/cancel", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
                }
                finally { release.TrySetResult(); }
                await first.ConfigureAwait(false);
                JobRun final = await RunAsync(f, id, ct).ConfigureAwait(false);
                Check.Equal(cancel ? JobRunStatusEnum.Cancelled : JobRunStatusEnum.Succeeded, final.Status, "Late plan preserves terminal cancellation or completes normally");
                Check.Equal(cancel ? 0 : 1, final.ItemsTotal, "Cancelled plans publish no items");
            }
            Check.Equal(1, handler.Executed.Count, "Only the uncancelled plan ran");
        }

        private static async Task PlanningLeaseAsync(HttpFixture f, CancellationToken ct)
        {
            DateTime now = DateTime.UtcNow;
            TestJobHandler handler = Register(f, "ok-recovered");
            string id = await RunNowAsync(f, handler.Key, ct).ConfigureAwait(false);
            JobRun stale = (await f.Database.Jobs.ClaimQueuedRunsAsync("stopped", now, 1, ct).ConfigureAwait(false)).Single();
            Check.Equal(0, (await f.Database.Jobs.ClaimQueuedRunsAsync("other", now.AddMinutes(4), 1, ct).ConfigureAwait(false)).Count, "Live planning lease excludes other workers");
            JobRun current = (await f.Database.Jobs.ClaimQueuedRunsAsync("recovery", now.AddMinutes(6), 1, ct).ConfigureAwait(false)).Single();
            Check.True(stale.PlanningToken != current.PlanningToken, "Recovered claim has a new fencing token");
            stale.Status = JobRunStatusEnum.Running;
            List<JobItem> staleItems = new List<JobItem> { new JobItem { Id = "jbi_stale", RunId = id, Key = handler.Key, TargetKey = "stale", NextAttemptUtc = now, CreatedUtc = now } };
            Check.True(!await f.Database.Jobs.FinishPlanningAsync(stale, staleItems, ct).ConfigureAwait(false), "Stale planner cannot publish");
            current.Status = JobRunStatusEnum.Running;
            List<JobItem> recovered = new List<JobItem> { new JobItem { Id = "jbi_recovered", RunId = id, Key = handler.Key, TargetKey = "ok-recovered", NextAttemptUtc = now, CreatedUtc = now } };
            Check.True(await f.Database.Jobs.FinishPlanningAsync(current, recovered, ct).ConfigureAwait(false), "Current planner publishes atomically");
            await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
            Check.Equal(JobRunStatusEnum.Succeeded, (await RunAsync(f, id, ct).ConfigureAwait(false)).Status, "Recovered run finishes");
            Check.True(handler.Executed.SequenceEqual(new[] { "ok-recovered" }), "No stale items leaked into execution");
        }

        private static async Task CancelSummaryAsync(HttpFixture f, CancellationToken ct)
        {
            TestJobHandler handler = Register(f, "ok-summarized");
            TaskCompletionSource started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            handler.SummaryBehavior = async (context, token) =>
            {
                started.TrySetResult();
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                return "{}";
            };
            string id = await RunNowAsync(f, handler.Key, ct).ConfigureAwait(false);
            Task<int> processing = f.Services.Jobs.ProcessAsync(ct);
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                await Api.ExpectAsync(f, HttpMethod.Post, Jobs + "/runs/" + id + "/cancel", HttpFixture.AdminToken, new { }, HttpStatusCode.OK, ct).ConfigureAwait(false);
            }
            finally { release.TrySetResult(); }
            await processing.ConfigureAwait(false);
            Check.Equal(JobRunStatusEnum.Cancelled, (await RunAsync(f, id, ct).ConfigureAwait(false)).Status, "Summary cannot revive a cancelled run");
            DateTime now = DateTime.UtcNow;
            // A stale caller must refuse even a pending item left by an older server version or an interrupted repair.
            await f.Database.Jobs.InsertItemsAsync(new List<JobItem> { new JobItem { Id="jbi_after_cancel", RunId=id, Key=handler.Key, TargetKey="late", CreatedUtc=now, NextAttemptUtc=now } }, ct).ConfigureAwait(false);
            List<JobItem> claimed = await f.Database.Jobs.ClaimItemsAsync(id, "stale-worker", now, now.AddMinutes(10), 1, false, ct).ConfigureAwait(false);
            Check.Equal(0, claimed.Count, "A cancelled run cannot be claimed from a stale running-list snapshot");
        }

        private static async Task AttemptFenceAsync(HttpFixture f, CancellationToken ct)
        {
            DateTime now = DateTime.UtcNow;
            TestJobHandler handler = Register(f, "ok-item");
            string id = await RunNowAsync(f, handler.Key, ct).ConfigureAwait(false);
            JobRun run = (await f.Database.Jobs.ClaimQueuedRunsAsync("planner", now, 1, ct).ConfigureAwait(false)).Single();
            run.Status = JobRunStatusEnum.Running;
            await f.Database.Jobs.FinishPlanningAsync(run, new List<JobItem> { new JobItem { Id="jbi_fenced", RunId=id, Key=handler.Key, TargetKey="ok-item", MaxAttempts=3, CreatedUtc=now, NextAttemptUtc=now } }, ct).ConfigureAwait(false);
            JobItem stale = (await f.Database.Jobs.ClaimItemsAsync(id, "same-worker", now, now.AddMinutes(1), 1, false, ct).ConfigureAwait(false)).Single();
            now = now.AddMinutes(2);
            await f.Database.Jobs.ReleaseExpiredItemsAsync(now, ct).ConfigureAwait(false);
            JobItem current = (await f.Database.Jobs.ClaimItemsAsync(id, "same-worker", now, now.AddMinutes(1), 1, false, ct).ConfigureAwait(false)).Single();
            stale.Status = JobItemStatusEnum.Succeeded;
            stale.CostMicros = 100;
            current.Status = JobItemStatusEnum.Succeeded;
            current.CostMicros = 2;
            Check.True(!await f.Database.Jobs.CompleteItemAsync(stale, ct).ConfigureAwait(false), "Old attempt cannot overwrite its replacement");
            Check.True(await f.Database.Jobs.CompleteItemAsync(current, ct).ConfigureAwait(false), "Current attempt can complete");
            Check.Equal(2L, (await f.Database.Jobs.CountItemsAsync(id, ct).ConfigureAwait(false)).CostMicros, "Stale cost was not written");
        }

        private static async Task LeaseClockAsync(HttpFixture f, CancellationToken ct)
        {
            DateTime now = DateTime.UtcNow;
            f.Services.Jobs.Clock = () => now;
            TestJobHandler first = Register(f, "ok-first");
            TestJobHandler second = Register(f, "ok-second");
            first.Behavior = context => { now = now.AddMinutes(f.Settings.Jobs.ItemLockMinutes + 1); return JobItemOutcome.Ok(); };
            bool fresh = false;
            second.Behavior = context => { fresh = context.Item.LockedUntilUtc > now; return JobItemOutcome.Ok(); };
            await RunNowAsync(f, first.Key, ct).ConfigureAwait(false);
            now = now.AddSeconds(1);
            await RunNowAsync(f, second.Key, ct).ConfigureAwait(false);
            await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
            Check.True(fresh, "The second item's deadline is measured when it is claimed");
        }

        private static async Task BudgetBetweenRunsAsync(HttpFixture f, CancellationToken ct)
        {
            TestJobHandler first = Register(f, "ok-first");
            TestJobHandler second = Register(f, "ok-second");
            first.CostMicrosPerItem = 10;
            second.CostMicrosPerItem = 10;
            f.Settings.Jobs.MonthlyBudgetUsd = 0.00001;
            await RunNowAsync(f, first.Key, ct).ConfigureAwait(false);
            string secondId = await RunNowAsync(f, second.Key, ct).ConfigureAwait(false);
            await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
            Check.Equal(1, first.Executed.Count, "First run consumes the cap");
            Check.Equal(0, second.Executed.Count, "The next run observes the new spend");
            Check.Equal(1, (await RunAsync(f, secondId, ct).ConfigureAwait(false)).ItemsSkipped, "Remaining items skipped");
        }

        private static async Task ExpiredClaimAsync(HttpFixture f, CancellationToken ct)
        {
            DateTime now = DateTime.UtcNow;
            f.Services.Jobs.Clock = () => now;
            TestJobHandler handler = Register(f, "ok-1");
            string runId = await RunNowAsync(f, handler.Key, ct).ConfigureAwait(false);
            await f.Services.Database.Jobs.ClaimQueuedRunsAsync("stopped-worker", now, 10, ct).ConfigureAwait(false);
            JobRun run = await RunAsync(f, runId, ct).ConfigureAwait(false);
            run.Status = JobRunStatusEnum.Running;
            await f.Services.Database.Jobs.FinishPlanningAsync(run, new List<JobItem> { new JobItem { Id = "jbi_" + Guid.NewGuid().ToString("N").Substring(0, 20), RunId = runId, Key = handler.Key, TargetKey = "ok-1", MaxAttempts = 3, NextAttemptUtc = now, CreatedUtc = now } }, ct).ConfigureAwait(false);
            List<JobItem> claimed = await f.Services.Database.Jobs.ClaimItemsAsync(runId, "stopped-worker", now, now.AddMinutes(1), 5, false, ct).ConfigureAwait(false);
            Check.Equal(1, claimed.Count, "Claimed by a worker that then stops");
            await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
            Check.Equal(0, handler.Executed.Count, "Not taken while the claim is live");
            now = now.AddMinutes(2);
            await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
            Check.Equal(1, handler.Executed.Count, "Taken over after the claim expired");
            run = await RunAsync(f, runId, ct).ConfigureAwait(false);
            Check.Equal(JobRunStatusEnum.Succeeded, run.Status, "The run finished");
        }

        private static async Task PruneAsync(HttpFixture f, CancellationToken ct)
        {
            TestJobHandler handler = Register(f, "ok-1");
            string oldRun = await RunNowAsync(f, handler.Key, ct).ConfigureAwait(false);
            await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
            string recentRun = await RunNowAsync(f, handler.Key, ct).ConfigureAwait(false);
            await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
            await f.Raw.ExecuteQueryAsync("UPDATE jobruns SET completedutc = NOW() - INTERVAL '200 days', createdutc = NOW() - INTERVAL '200 days' WHERE id = '" + oldRun + "';", false, ct).ConfigureAwait(false);
            string prune = await RunNowAsync(f, "jobs.prune", ct).ConfigureAwait(false);
            await f.Services.Jobs.ProcessAsync(ct).ConfigureAwait(false);
            Check.Equal(JobRunStatusEnum.Succeeded, (await RunAsync(f, prune, ct).ConfigureAwait(false)).Status, "Prune succeeded");
            Check.True(await f.Services.Database.Jobs.ReadRunAsync(oldRun, ct).ConfigureAwait(false) == null, "The old run was deleted");
            Check.True(await f.Services.Database.Jobs.ReadRunAsync(recentRun, ct).ConfigureAwait(false) != null, "The recent run was kept");
            Check.Equal(0L, await ContentTestKit.ScalarAsync(f, "SELECT COUNT(*) AS cnt FROM jobitems WHERE runid = '" + oldRun + "';", ct).ConfigureAwait(false), "Its items were deleted");
        }

        private static Task ArithmeticAsync(HttpFixture f, CancellationToken ct)
        {
            DateTime at = new DateTime(2026, 11, 21, 10, 17, 42, DateTimeKind.Utc);
            Check.Equal(new DateTime(2026, 11, 21, 10, 32, 0, DateTimeKind.Utc), JobSchedule.Next(new JobDefinition { IntervalType = JobIntervalTypeEnum.EveryNMinutes, IntervalValue = 15 }, at), "Every 15 minutes");
            Check.Equal(new DateTime(2026, 11, 21, 16, 17, 0, DateTimeKind.Utc), JobSchedule.Next(new JobDefinition { IntervalType = JobIntervalTypeEnum.EveryNHours, IntervalValue = 6 }, at), "Every 6 hours");
            Check.Equal(new DateTime(2026, 11, 21, 11, 0, 0, DateTimeKind.Utc), JobSchedule.Next(new JobDefinition { IntervalType = JobIntervalTypeEnum.EveryNDays, IntervalValue = 1, TimeOfDayMinutesUtc = 660 }, at), "Daily at 11:00 today");
            Check.Equal(new DateTime(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc), JobSchedule.Next(new JobDefinition { IntervalType = JobIntervalTypeEnum.EveryNDays, IntervalValue = 1, TimeOfDayMinutesUtc = 540 }, at), "Daily at 09:00 tomorrow");
            Check.Equal(new DateTime(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc), JobSchedule.Next(new JobDefinition { IntervalType = JobIntervalTypeEnum.EveryNDays, IntervalValue = 3, TimeOfDayMinutesUtc = 540, LastRunUtc = at }, at), "Every 3 days from the last run");
            Check.Equal(new DateTime(2026, 11, 23, 6, 0, 0, DateTimeKind.Utc), JobSchedule.Next(new JobDefinition { IntervalType = JobIntervalTypeEnum.DaysOfWeek, DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Thursday }, TimeOfDayMinutesUtc = 360 }, at), "Next Monday (2026-11-21 is a Saturday)");
            Check.True(JobSchedule.Next(new JobDefinition { IntervalType = JobIntervalTypeEnum.Manual }, at) == null, "Manual never fires");
            Check.True(JobSchedule.Next(new JobDefinition { IntervalType = JobIntervalTypeEnum.DaysOfWeek }, at) == null, "No days, no run");
            return Task.CompletedTask;
        }

        private static async Task AccessAsync(HttpFixture f, CancellationToken ct)
        {
            TestMember member = await Api.MemberAsync(f, ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, Jobs, member.AccessToken, null, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Jobs + "/jobs.prune/run", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Post, Jobs + "/runs/enumerate", member.AccessToken, new { }, HttpStatusCode.Forbidden, "Forbidden", ct).ConfigureAwait(false);
            JsonElement list = await Api.ExpectAsync(f, HttpMethod.Get, Jobs, HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            JsonElement prune = list.GetProperty("objects").EnumerateArray().First(j => Api.Str(j, "key") == "jobs.prune");
            Check.True(prune.GetProperty("enabled").GetBoolean(), "The built-in prune job is scheduled");
            Check.Equal("EveryNDays", Api.Str(prune, "intervalType"), "Daily");
            Check.True(prune.GetProperty("handlerAvailable").GetBoolean(), "Handler registered");
            await Api.ExpectErrorAsync(f, HttpMethod.Get, Jobs + "/no.such", HttpFixture.AdminToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            await Api.ExpectErrorAsync(f, HttpMethod.Get, Jobs + "/runs/jbr_missing", HttpFixture.AdminToken, null, HttpStatusCode.NotFound, "NotFound", ct).ConfigureAwait(false);
            JsonElement overview = await Api.ExpectAsync(f, HttpMethod.Get, Jobs + "/overview", HttpFixture.AdminToken, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
            Check.Equal(2000d, overview.GetProperty("monthlyBudgetUsd").GetDouble(), "Default monthly cap");
            Check.True(!overview.GetProperty("runsHarnesses").GetBoolean(), "Harnesses are off by default");
        }

        #endregion
    }
}
