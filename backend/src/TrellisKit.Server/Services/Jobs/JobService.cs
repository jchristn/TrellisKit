namespace TrellisKit.Server.Services.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The job engine of one property (docs/INTEGRATION.md): seeds a definition per registered handler,
    /// fires schedules into runs, plans runs into items, executes items with retries, backoff, a dead-letter state,
    /// per-run concurrency, a per-run budget, and the monthly cap across jobs, and serves the admin operations. Every
    /// claim uses FOR UPDATE SKIP LOCKED, so any number of instances can run passes. Thread safety: safe for concurrent
    /// use.
    /// </summary>
    public class JobService
    {
        #region Public-Members

        /// <summary>
        /// Most items one run may plan.
        /// </summary>
        public const int MaxItemsPerRun = 20000;

        /// <summary>
        /// Handlers of this property.
        /// </summary>
        public JobRegistry Registry { get; } = new JobRegistry();

        /// <summary>
        /// Clock used for scheduling and backoff (tests replace it). Default UTC now.
        /// </summary>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? throw new ArgumentNullException(nameof(Clock)); }
        }

        /// <summary>
        /// Identifier of this instance's worker.
        /// </summary>
        public string WorkerId { get; } = "jobs-" + Environment.MachineName + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        #endregion

        #region Private-Members

        private const int _MaxPayloadChars = 65536;
        private readonly ApplicationServices _Services;
        private readonly SemaphoreSlim _Seed = new SemaphoreSlim(1, 1);
        private int _SeededVersion = -1;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and register the built-in jobs.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">services is null.</exception>
        public JobService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            Registry.Register(new PruneJobHandler());
            Registry.Register(new AiCallPruneJobHandler());
            Registry.Register(new ScopeBenchmarkJobHandler());
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create definitions for handlers registered since the last call.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task EnsureDefinitionsAsync(CancellationToken token = default)
        {
            int version = Registry.Version;
            if (version == _SeededVersion) return;
            await _Seed.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (version == _SeededVersion) return;
                DateTime now = Clock();
                foreach (IJobHandler handler in Registry.All())
                {
                    JobHandlerDefaults d = handler.Defaults;
                    JobDefinition definition = new JobDefinition
                    {
                        Id = IdGenerator.Generate(JobIdPrefixes.Definition),
                        Key = handler.Key,
                        Name = d.Name,
                        Description = d.Description,
                        Enabled = d.Enabled,
                        IntervalType = d.IntervalType,
                        IntervalValue = d.IntervalValue,
                        TimeOfDayMinutesUtc = d.TimeOfDayMinutesUtc,
                        DaysOfWeek = new List<DayOfWeek>(d.DaysOfWeek),
                        MaxConcurrency = d.MaxConcurrency,
                        MaxAttempts = d.MaxAttempts,
                        BudgetMicrosPerRun = d.BudgetUsdPerRun == null ? null : Micros(d.BudgetUsdPerRun.Value),
                        RequiresHarness = d.RequiresHarness,
                        CreatedUtc = now,
                        UpdatedUtc = now
                    };
                    definition.NextRunUtc = definition.Enabled ? JobSchedule.Next(definition, now) : null;
                    await _Services.Database.Jobs.InsertDefinitionIfMissingAsync(definition, token).ConfigureAwait(false);
                }
                _SeededVersion = version;
            }
            finally
            {
                _Seed.Release();
            }
        }

        /// <summary>
        /// Fire due schedules: queue a run, or record a skipped run when the previous run is still active or the monthly
        /// cap is reached.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Runs queued.</returns>
        public async Task<int> TickAsync(CancellationToken token = default)
        {
            if (!_Services.Settings.Jobs.Enabled) return 0;
            await EnsureDefinitionsAsync(token).ConfigureAwait(false);
            DateTime now = Clock();
            int queued = 0;
            foreach (JobDefinition definition in await _Services.Database.Jobs.ClaimDueDefinitionsAsync(now, now.AddMinutes(2), 50, token).ConfigureAwait(false))
            {
                try
                {
                    if (Registry.Find(definition.Key) == null) continue;
                    string? skip = await _Services.Database.Jobs.HasActiveRunAsync(definition.Key, token).ConfigureAwait(false) ? "The previous run was still active."
                        : await MonthlyCapReachedAsync(now, token).ConfigureAwait(false) ? "The monthly job budget was reached." : null;
                    JobRun run = NewRun(definition, JobTriggerEnum.Schedule, false, null, now);
                    if (skip != null)
                    {
                        run.Status = JobRunStatusEnum.Skipped;
                        run.SkippedReason = skip;
                        run.CompletedUtc = now;
                    }
                    await _Services.Database.Jobs.CreateRunAsync(run, token).ConfigureAwait(false);
                    TrellisKitTelemetry.Add(JobTelemetry.Runs, 1, "key", definition.Key, "outcome", skip == null ? "queued" : skip.StartsWith("The previous", StringComparison.Ordinal) ? "skipped_active" : "skipped_budget");
                    if (skip == null) queued++;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _Services.Logger.Warn("[JobService] schedule " + definition.Key + " failed to fire: " + e.Message);
                }
                finally
                {
                    definition.LastRunUtc = now;
                    definition.NextRunUtc = definition.Enabled ? JobSchedule.Next(definition, now) : null;
                    definition.LockedUntilUtc = null;
                    await _Services.Database.Jobs.UpdateDefinitionAsync(definition, CancellationToken.None).ConfigureAwait(false);
                }
            }

            return queued;
        }

        /// <summary>
        /// One processing pass: release expired claims, plan queued runs, execute due items of running runs within
        /// their concurrency and budgets, and finish runs with no open items.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Items attempted.</returns>
        public async Task<int> ProcessAsync(CancellationToken token = default)
        {
            if (!_Services.Settings.Jobs.Enabled) return 0;
            await EnsureDefinitionsAsync(token).ConfigureAwait(false);
            DateTime now = Clock();
            await _Services.Database.Jobs.ReleaseExpiredItemsAsync(now, token).ConfigureAwait(false);
            Dictionary<string, JobDefinition> definitions = new Dictionary<string, JobDefinition>(StringComparer.Ordinal);
            for (int planned = 0; planned < _Services.Settings.Jobs.MaxRunsPerPass; planned++)
            {
                List<JobRun> claims = await _Services.Database.Jobs.ClaimQueuedRunsAsync(WorkerId, Clock(), 1, token).ConfigureAwait(false);
                if (claims.Count == 0) break;
                JobDefinition? claimedDefinition = await _Services.Database.Jobs.ReadDefinitionAsync(claims[0].Key, token).ConfigureAwait(false);
                if (claimedDefinition != null) definitions[claimedDefinition.Key] = claimedDefinition;
                await PlanAsync(claims[0], definitions, Clock(), token).ConfigureAwait(false);
            }

            now = Clock();
            int attempted = 0;
            foreach (JobRun run in await _Services.Database.Jobs.ReadRunningRunsAsync(200, token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                if (!definitions.TryGetValue(run.Key, out JobDefinition? definition))
                {
                    definition = await _Services.Database.Jobs.ReadDefinitionAsync(run.Key, token).ConfigureAwait(false);
                    if (definition == null) continue;
                    definitions[run.Key] = definition;
                }
                IJobHandler? handler = Registry.Find(run.Key);
                if (handler == null) continue;
                now = Clock();
                if (await MonthlyCapReachedAsync(Clock(), token).ConfigureAwait(false))
                {
                    await _Services.Database.Jobs.SkipPendingItemsAsync(run.Id, "The monthly job budget was reached.", now, token).ConfigureAwait(false);
                }
                else if (definition.BudgetMicrosPerRun != null && (await _Services.Database.Jobs.CountItemsAsync(run.Id, token).ConfigureAwait(false)).CostMicros >= definition.BudgetMicrosPerRun.Value)
                {
                    await _Services.Database.Jobs.SkipPendingItemsAsync(run.Id, "The run budget was reached.", now, token).ConfigureAwait(false);
                }
                else
                {
                    bool harness = _Services.Settings.Jobs.RunHarnesses;
                    DateTime lockUntil = now.AddMinutes(_Services.Settings.Jobs.ItemLockMinutes);
                    List<JobItem> items = await _Services.Database.Jobs.ClaimItemsAsync(run.Id, WorkerId, now, lockUntil, definition.MaxConcurrency, harness, token).ConfigureAwait(false);
                    attempted += items.Count;
                    await Task.WhenAll(items.Select(i => ExecuteAsync(handler, definition, run, i, token))).ConfigureAwait(false);
                }
                await FinishIfDoneAsync(run, token).ConfigureAwait(false);
            }

            return attempted;
        }

        /// <summary>
        /// GET /api/v1/admin/jobs (SystemAdmin): paged definitions with their latest runs.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Definitions.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public async Task<EnumerationResult<JobDefinitionView>> EnumerateAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            RequireAdmin(actor);
            await EnsureDefinitionsAsync(token).ConfigureAwait(false);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, JobCollections.AdminDefinitions, true);
            EnumerationResult<JobDefinition> page = await _Services.Database.Jobs.EnumerateDefinitionsAsync(request, token).ConfigureAwait(false);
            Dictionary<string, JobRun> latest = await _Services.Database.Jobs.ReadLatestRunsAsync(page.Objects.Select(d => d.Key).ToList(), token).ConfigureAwait(false);
            List<JobDefinitionView> views = page.Objects.Select(d => View(d, latest.TryGetValue(d.Key, out JobRun? r) ? r : null)).ToList();
            await _Services.Metadata.AttachAsync(TargetTypeEnum.JobDefinition, views, d => d.Id, true, token).ConfigureAwait(false);
            return page.With(views);
        }

        /// <summary>
        /// GET /api/v1/admin/jobs/{key} (SystemAdmin).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Definition.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<JobDefinitionView> GetAsync(ContentActor actor, string key, CancellationToken token = default)
        {
            RequireAdmin(actor);
            JobDefinition definition = await RequireDefinitionAsync(key, token).ConfigureAwait(false);
            Dictionary<string, JobRun> latest = await _Services.Database.Jobs.ReadLatestRunsAsync(new[] { key }, token).ConfigureAwait(false);
            return await _Services.Metadata.AttachOneAsync(TargetTypeEnum.JobDefinition, View(definition, latest.TryGetValue(definition.Key, out JobRun? r) ? r : null), definition.Id, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/jobs/{key} (SystemAdmin): change the schedule, budgets, and concurrency. Audited as job.update.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="key">Key.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Definition.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<JobDefinitionView> UpdateAsync(ContentActor actor, RequestInfo info, string key, JobDefinitionRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            RequireAdmin(actor);
            JobDefinition definition = await RequireDefinitionAsync(key, token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            JobIntervalTypeEnum type = definition.IntervalType;
            if (request.IntervalType != null && !Enum.TryParse(request.IntervalType, true, out type)) errors["intervalType"] = "EveryNMinutes, EveryNHours, EveryNDays, DaysOfWeek, or Manual.";
            int value = request.IntervalValue ?? definition.IntervalValue;
            List<DayOfWeek> days = definition.DaysOfWeek;
            if (request.DaysOfWeek != null)
            {
                days = new List<DayOfWeek>();
                foreach (string name in request.DaysOfWeek)
                {
                    if (Enum.TryParse(name, true, out DayOfWeek day) && Enum.IsDefined(day)) days.Add(day);
                    else errors["daysOfWeek"] = "Day names Sunday to Saturday.";
                }
            }
            if (!errors.ContainsKey("intervalType"))
            {
                string? problem = JobSchedule.Problem(type, value, days);
                if (problem != null) errors[type == JobIntervalTypeEnum.DaysOfWeek ? "daysOfWeek" : "intervalValue"] = problem;
            }
            if (request.TimeOfDayMinutesUtc != null && (request.TimeOfDayMinutesUtc < 0 || request.TimeOfDayMinutesUtc > 1439)) errors["timeOfDayMinutesUtc"] = "0 to 1439.";
            if (request.MaxConcurrency != null && (request.MaxConcurrency < 1 || request.MaxConcurrency > 64)) errors["maxConcurrency"] = "1 to 64.";
            if (request.MaxAttempts != null && (request.MaxAttempts < 1 || request.MaxAttempts > 10)) errors["maxAttempts"] = "1 to 10.";
            if (request.BudgetUsdPerRun != null && (request.BudgetUsdPerRun < 0 || request.BudgetUsdPerRun > 100000 || Double.IsNaN(request.BudgetUsdPerRun.Value))) errors["budgetUsdPerRun"] = "0 (no cap) to 100000.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);

            DateTime now = Clock();
            definition.IntervalType = type;
            definition.IntervalValue = value;
            definition.DaysOfWeek = days;
            if (request.Enabled != null) definition.Enabled = request.Enabled.Value;
            if (request.TimeOfDayMinutesUtc != null) definition.TimeOfDayMinutesUtc = request.TimeOfDayMinutesUtc.Value;
            if (request.MaxConcurrency != null) definition.MaxConcurrency = request.MaxConcurrency.Value;
            if (request.MaxAttempts != null) definition.MaxAttempts = request.MaxAttempts.Value;
            if (request.BudgetUsdPerRun != null) definition.BudgetMicrosPerRun = request.BudgetUsdPerRun.Value <= 0 ? null : Micros(request.BudgetUsdPerRun.Value);
            definition.NextRunUtc = definition.Enabled ? JobSchedule.Next(definition, now) : null;
            definition.UpdatedByUserId = actor.UserId;
            definition.UpdatedUtc = now;
            await _Services.Database.Jobs.UpdateDefinitionAsync(definition, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "job.update", "JobDefinition", definition.Key, definition.Name, token).ConfigureAwait(false);
            return await GetAsync(actor, definition.Key, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/jobs/{key}/run (SystemAdmin): queue a run now. Audited as job.run.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="key">Key.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued run.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 DryRunNotSupported, 403 Forbidden, 404 NotFound, 409 RunActive, 409 HandlerMissing, 409 BudgetExceeded.</exception>
        public Task<JobRunView> RunNowAsync(ContentActor actor, RequestInfo info, string key, JobRunRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return QueueAsync(actor, info, key, null, request.DryRun, token);
        }

        /// <summary>
        /// Queue a run with handler-specific parameters (SystemAdmin), for features that start a job on an
        /// administrator's behalf (for example the scope benchmark of a prompt draft). Same checks and audit as
        /// <see cref="RunNowAsync"/>.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="key">Key.</param>
        /// <param name="parameters">Parameters JSON for the handler, or null.</param>
        /// <param name="dryRun">Dry run.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued run.</returns>
        /// <exception cref="ArgumentNullException">info is null.</exception>
        /// <exception cref="ApiErrorException">400 DryRunNotSupported, 403 Forbidden, 404 NotFound, 409 RunActive, 409 HandlerMissing, 409 BudgetExceeded.</exception>
        public async Task<JobRunView> QueueAsync(ContentActor actor, RequestInfo info, string key, string? parameters, bool dryRun, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            RequireAdmin(actor);
            JobDefinition definition = await RequireDefinitionAsync(key, token).ConfigureAwait(false);
            IJobHandler handler = Registry.Find(definition.Key) ?? throw new ApiErrorException(409, "HandlerMissing", "No handler is registered for this job on this server version.");
            if (dryRun && !handler.Defaults.SupportsDryRun) throw new ApiErrorException(400, "DryRunNotSupported", "This job does not support dry runs.");
            if (await _Services.Database.Jobs.HasActiveRunAsync(definition.Key, token).ConfigureAwait(false)) throw new ApiErrorException(409, "RunActive", "A run of this job is already queued or running.");
            DateTime now = Clock();
            if (await MonthlyCapReachedAsync(now, token).ConfigureAwait(false)) throw new ApiErrorException(409, "BudgetExceeded", "The monthly job budget is used up. Raise jobs.monthlyBudgetUsd or wait for next month.");
            JobRun run = NewRun(definition, dryRun ? JobTriggerEnum.DryRun : JobTriggerEnum.Manual, dryRun, actor.UserId, now);
            run.Parameters = parameters;
            await _Services.Database.Jobs.CreateRunAsync(run, token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(JobTelemetry.Runs, 1, "key", definition.Key, "outcome", "queued");
            await _Services.Audit.WriteAsync(info, "job.run", "JobRun", run.Id, definition.Key + (dryRun ? " (dry run)" : String.Empty), token).ConfigureAwait(false);
            return RunView(run, null);
        }

        /// <summary>
        /// The newest run of a key whose parameters hold a string property with a value, or null (no administrator check;
        /// callers authorize).
        /// </summary>
        /// <param name="key">Handler key.</param>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Parameter value.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Run view or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        public async Task<JobRunView?> FindLatestRunAsync(string key, string name, string value, CancellationToken token = default)
        {
            JobRun? run = await _Services.Database.Jobs.ReadLatestRunWithParameterAsync(key, name, value, token).ConfigureAwait(false);
            return run == null ? null : RunView(run, null);
        }

        /// <summary>
        /// POST /api/v1/admin/jobs/runs/{id}/cancel (SystemAdmin): stop a queued or running run; pending items are
        /// skipped and items already running finish. Audited as job.cancel.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The run.</returns>
        /// <exception cref="ArgumentNullException">info is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound, 409 RunFinished.</exception>
        public async Task<JobRunView> CancelAsync(ContentActor actor, RequestInfo info, string runId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            RequireAdmin(actor);
            JobRun run = await RequireRunAsync(runId, token).ConfigureAwait(false);
            if (run.Status != JobRunStatusEnum.Queued && run.Status != JobRunStatusEnum.Running) throw new ApiErrorException(409, "RunFinished", "The run has already finished.");
            DateTime now = Clock();
            if (!await _Services.Database.Jobs.CancelRunAsync(run.Id, now, token).ConfigureAwait(false)) throw new ApiErrorException(409, "RunFinished", "The run has already finished.");
            run = await RequireRunAsync(run.Id, token).ConfigureAwait(false);
            JobItemCounts counts = await _Services.Database.Jobs.CountItemsAsync(run.Id, token).ConfigureAwait(false);
            TrellisKitTelemetry.Add(JobTelemetry.Runs, 1, "key", run.Key, "outcome", "cancelled");
            await _Services.Audit.WriteAsync(info, "job.cancel", "JobRun", run.Id, run.Key, token).ConfigureAwait(false);
            return RunView(run, counts);
        }

        /// <summary>
        /// GET /api/v1/admin/jobs/runs/{id} (SystemAdmin).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Run with live counts.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<JobRunView> GetRunAsync(ContentActor actor, string runId, CancellationToken token = default)
        {
            RequireAdmin(actor);
            JobRun run = await RequireRunAsync(runId, token).ConfigureAwait(false);
            return RunView(run, await _Services.Database.Jobs.CountItemsAsync(run.Id, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/admin/jobs/runs/enumerate (SystemAdmin): newest first; filters key, status, trigger.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<EnumerationResult<JobRunView>> EnumerateRunsAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            RequireAdmin(actor);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, JobCollections.AdminRuns, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            List<JobRunStatusEnum> statuses = DirectoryFilters.Enums<JobRunStatusEnum>(request.Query, "status", errors);
            List<JobTriggerEnum> triggers = DirectoryFilters.Enums<JobTriggerEnum>(request.Query, "trigger", errors);
            DirectoryFilters.ThrowIfAny(errors);
            EnumerationResult<JobRun> page = await _Services.Database.Jobs.EnumerateRunsAsync(request, request.Query.FilterString("key"), statuses, triggers, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(r => RunView(r, null)).ToList());
        }

        /// <summary>
        /// POST /api/v1/admin/jobs/items/enumerate (SystemAdmin): newest first; filters runId, key, status
        /// (status Dead is the dead-letter view).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query, or null.</param>
        /// <param name="runId">Run identifier from the path, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<EnumerationResult<JobItemView>> EnumerateItemsAsync(ContentActor actor, EnumerationQuery? query, string? runId, CancellationToken token = default)
        {
            RequireAdmin(actor);
            if (runId != null) await RequireRunAsync(runId, token).ConfigureAwait(false);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, JobCollections.AdminItems, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            List<JobItemStatusEnum> statuses = DirectoryFilters.Enums<JobItemStatusEnum>(request.Query, "status", errors);
            DirectoryFilters.ThrowIfAny(errors);
            EnumerationResult<JobItem> page = await _Services.Database.Jobs.EnumerateItemsAsync(request, runId ?? request.Query.FilterString("runId"), request.Query.FilterString("key"), statuses, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(ItemView).ToList());
        }

        /// <summary>
        /// POST /api/v1/admin/jobs/items/{id}/retry (SystemAdmin): return a dead or failed item to pending with its
        /// attempts reset, reopening its run. Audited as job.item.retry.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="itemId">Item identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The item.</returns>
        /// <exception cref="ArgumentNullException">info is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound, 409 NotRetryable.</exception>
        public async Task<JobItemView> RetryItemAsync(ContentActor actor, RequestInfo info, string itemId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            RequireAdmin(actor);
            JobItem item = await _Services.Database.Jobs.ReadItemAsync(itemId ?? String.Empty, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Job item");
            JobRun run = await RequireRunAsync(item.RunId, token).ConfigureAwait(false);
            if (run.Status == JobRunStatusEnum.Cancelled || run.Status == JobRunStatusEnum.Skipped || run.Status == JobRunStatusEnum.Queued)
                throw new ApiErrorException(409, "NotRetryable", "Items of a cancelled, skipped, or queued run cannot be retried.");
            if (!await _Services.Database.Jobs.RetryItemAsync(item.Id, Clock(), token).ConfigureAwait(false))
                throw new ApiErrorException(409, "NotRetryable", "Only dead or failed items can be retried.");
            await _Services.Audit.WriteAsync(info, "job.item.retry", "JobItem", item.Id, item.Key + " " + item.TargetKey, token).ConfigureAwait(false);
            return ItemView(await _Services.Database.Jobs.ReadItemAsync(item.Id, token).ConfigureAwait(false) ?? item);
        }

        /// <summary>
        /// GET /api/v1/admin/jobs/overview (SystemAdmin): the month's cost against the cap and open work.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Overview.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public async Task<JobOverviewView> OverviewAsync(ContentActor actor, CancellationToken token = default)
        {
            RequireAdmin(actor);
            await EnsureDefinitionsAsync(token).ConfigureAwait(false);
            DateTime now = Clock();
            DateTime month = MonthStart(now);
            long cost = await _Services.Database.Jobs.CostMicrosSinceAsync(month, token).ConfigureAwait(false);

            double cap = _Services.Settings.Jobs.MonthlyBudgetUsd;
            return new JobOverviewView
            {
                Enabled = _Services.Settings.Jobs.Enabled,
                RunsHarnesses = _Services.Settings.Jobs.RunHarnesses,
                MonthStartUtc = month,
                MonthCostUsd = Usd(cost),
                MonthlyBudgetUsd = cap,
                RemainingUsd = Math.Max(0, Math.Round(cap - Usd(cost), 6)),
                OpenRuns = await _Services.Database.Jobs.CountOpenRunsAsync(token).ConfigureAwait(false),
                Definitions = await _Services.Database.Jobs.CountDefinitionsAsync(false, token).ConfigureAwait(false),
                EnabledDefinitions = await _Services.Database.Jobs.CountDefinitionsAsync(true, token).ConfigureAwait(false)
            };
        }

        #endregion

        #region Private-Methods

        private static void RequireAdmin(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!actor.IsSystemAdmin) throw new ApiErrorException(403, "Forbidden", "Only administrators can read or change background jobs.");
        }

        private async Task<JobDefinition> RequireDefinitionAsync(string key, CancellationToken token)
        {
            await EnsureDefinitionsAsync(token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(key)) throw ApiErrorException.NotFound("Job");
            return await _Services.Database.Jobs.ReadDefinitionAsync(key.Trim(), token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Job");
        }

        private async Task<JobRun> RequireRunAsync(string runId, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(runId)) throw ApiErrorException.NotFound("Job run");
            return await _Services.Database.Jobs.ReadRunAsync(runId.Trim(), token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Job run");
        }

        private static JobRun NewRun(JobDefinition definition, JobTriggerEnum trigger, bool dryRun, string? userId, DateTime now)
        {
            return new JobRun
            {
                Id = IdGenerator.Generate(JobIdPrefixes.Run),
                DefinitionId = definition.Id,
                Key = definition.Key,
                Trigger = trigger,
                Status = JobRunStatusEnum.Queued,
                IsDryRun = dryRun,
                RequestedByUserId = userId,
                CreatedUtc = now
            };
        }

        private async Task PlanAsync(JobRun run, Dictionary<string, JobDefinition> definitions, DateTime now, CancellationToken token)
        {
            IJobHandler? handler = Registry.Find(run.Key);
            if (handler == null || !definitions.TryGetValue(run.Key, out JobDefinition? definition))
            {
                await FailAsync(run, "No handler is registered for this job on this server version.", now, token).ConfigureAwait(false);
                return;
            }

            List<JobItemPlan> plans;
            try
            {
                using CancellationTokenSource planning = CancellationTokenSource.CreateLinkedTokenSource(token);
                planning.CancelAfter(TimeSpan.FromMinutes(5));
                plans = await handler.PlanAsync(new JobRunContext(_Services, definition, run, now), planning.Token).WaitAsync(planning.Token).ConfigureAwait(false) ?? new List<JobItemPlan>();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                await FailAsync(run, "Planning failed: " + Truncate(e.Message, 1000), now, token).ConfigureAwait(false);
                return;
            }

            List<JobItem> items = new List<JobItem>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JobItemPlan plan in plans.Take(MaxItemsPerRun))
            {
                string target = Truncate(plan.TargetKey?.Trim() ?? String.Empty, 500);
                if (target.Length == 0 || !seen.Add(target)) continue;
                items.Add(new JobItem
                {
                    Id = IdGenerator.Generate(JobIdPrefixes.Item),
                    RunId = run.Id,
                    Key = run.Key,
                    TargetKey = target,
                    Payload = plan.Payload == null ? null : Truncate(plan.Payload, _MaxPayloadChars),
                    Status = JobItemStatusEnum.Pending,
                    MaxAttempts = definition.MaxAttempts,
                    NextAttemptUtc = now,
                    RequiresHarness = definition.RequiresHarness,
                    CreatedUtc = now
                });
            }
            run.ItemsTotal = items.Count;
            run.Status = JobRunStatusEnum.Running;
            if (plans.Count > MaxItemsPerRun) run.Error = "The plan listed " + plans.Count + " items; only the first " + MaxItemsPerRun + " were kept.";
            if (await _Services.Database.Jobs.FinishPlanningAsync(run, items, token).ConfigureAwait(false)) await FinishIfDoneAsync(run, token).ConfigureAwait(false);
        }

        private async Task FailAsync(JobRun run, string error, DateTime now, CancellationToken token)
        {
            run.Status = JobRunStatusEnum.Failed;
            run.Error = error;
            run.CompletedUtc = now;
            if (await _Services.Database.Jobs.FinishPlanningAsync(run, new List<JobItem>(), token).ConfigureAwait(false)) TrellisKitTelemetry.Add(JobTelemetry.Runs, 1, "key", run.Key, "outcome", "failed");
        }

        private async Task ExecuteAsync(IJobHandler handler, JobDefinition definition, JobRun run, JobItem item, CancellationToken token)
        {
            Stopwatch watch = Stopwatch.StartNew();
            JobItemOutcome outcome;
            long modelMicros;
            // Model calls inside the attempt record this job, run, and item in the call ledger, and their cost is
            // added to the item (handlers report only non-model costs in JobItemOutcome.CostMicros).
            using (Ai.AiCallScope scope = Ai.AiCallScope.BeginJob(item.Key, run.Id, item.Id))
            using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                limit.CancelAfter(TimeSpan.FromMinutes(_Services.Settings.Jobs.ItemLockMinutes).Subtract(TimeSpan.FromSeconds(30)).Duration());
                try
                {
                    outcome = await handler.ExecuteAsync(new JobItemContext(_Services, definition, run, item, Clock()), limit.Token).ConfigureAwait(false) ?? JobItemOutcome.Fail("The handler returned no outcome.");
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    outcome = JobItemOutcome.Fail("The attempt took longer than the item claim (jobs.itemLockMinutes).");
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    outcome = JobItemOutcome.Fail(e.GetType().Name + ": " + e.Message);
                }
                modelMicros = scope.CostMicros;
            }

            DateTime now = Clock();
            long attemptMicros = outcome.CostMicros + modelMicros;
            item.CostMicros += attemptMicros;
            item.Error = outcome.Success ? null : Truncate(outcome.Error ?? "Failed.", 2000);
            item.Result = outcome.Result == null ? null : Truncate(outcome.Result, _MaxPayloadChars);
            string label;
            if (outcome.Success)
            {
                item.Status = JobItemStatusEnum.Succeeded;
                item.CompletedUtc = now;
                label = "succeeded";
            }
            else if (outcome.Retryable && item.Attempts < item.MaxAttempts)
            {
                item.Status = JobItemStatusEnum.Pending;
                item.NextAttemptUtc = now.Add(Backoff(item.Attempts));
                label = "retry";
            }
            else if (outcome.Retryable)
            {
                item.Status = JobItemStatusEnum.Dead;
                item.CompletedUtc = now;
                label = "dead";
            }
            else
            {
                item.Status = JobItemStatusEnum.Failed;
                item.CompletedUtc = now;
                label = "failed";
            }
            if (!await _Services.Database.Jobs.CompleteItemAsync(item, CancellationToken.None).ConfigureAwait(false)) return;
            TrellisKitTelemetry.Add(JobTelemetry.Items, 1, "key", item.Key, "outcome", label);
            TrellisKitTelemetry.Record(JobTelemetry.ItemDuration, watch.Elapsed.TotalMilliseconds, "key", item.Key);
            if (attemptMicros > 0) TrellisKitTelemetry.Add(JobTelemetry.CostMicros, attemptMicros, "key", item.Key);
        }

        private TimeSpan Backoff(int attempts)
        {
            double baseSeconds = _Services.Settings.Jobs.RetryBaseSeconds * Math.Pow(2, Math.Max(0, attempts - 1));
            double jitter = 0.8 + (Random.Shared.NextDouble() * 0.4);
            return TimeSpan.FromSeconds(Math.Min(_Services.Settings.Jobs.RetryMaxSeconds, baseSeconds * jitter));
        }

        private async Task FinishIfDoneAsync(JobRun run, CancellationToken token)
        {
            JobRun? current = await _Services.Database.Jobs.ReadRunAsync(run.Id, token).ConfigureAwait(false);
            if (current == null || current.Status != JobRunStatusEnum.Running) return;
            JobItemCounts counts = await _Services.Database.Jobs.CountItemsAsync(current.Id, token).ConfigureAwait(false);
            Apply(current, counts);
            if (counts.Open > 0)
            {
                await _Services.Database.Jobs.UpdateRunningRunAsync(current, token).ConfigureAwait(false);
                return;
            }

            current.Status = counts.Dead + counts.Failed + counts.Skipped == 0 ? JobRunStatusEnum.Succeeded
                : counts.Succeeded == 0 && counts.Skipped == 0 ? JobRunStatusEnum.Failed
                : JobRunStatusEnum.PartiallySucceeded;
            current.CompletedUtc = Clock();
            if (Registry.Find(current.Key) is IJobRunSummarizer summarizer)
            {
                JobDefinition? definition = await _Services.Database.Jobs.ReadDefinitionAsync(current.Key, token).ConfigureAwait(false);
                try
                {
                    string? summary = definition == null ? null : await summarizer.SummarizeAsync(new JobRunContext(_Services, definition, current, current.CompletedUtc.Value), token).ConfigureAwait(false);
                    current.Summary = summary == null ? null : Truncate(summary, _MaxPayloadChars);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    current.Error = Truncate("Summary failed: " + e.Message, 1000);
                }
            }
            if (!await _Services.Database.Jobs.UpdateRunningRunAsync(current, token).ConfigureAwait(false)) return;
            string outcome = current.Status == JobRunStatusEnum.Succeeded ? "succeeded" : current.Status == JobRunStatusEnum.Failed ? "failed" : "partially_succeeded";
            TrellisKitTelemetry.Add(JobTelemetry.Runs, 1, "key", current.Key, "outcome", outcome);
        }

        private static void Apply(JobRun run, JobItemCounts counts)
        {
            run.ItemsTotal = Math.Max(run.ItemsTotal, counts.Total);
            run.ItemsSucceeded = counts.Succeeded;
            run.ItemsFailed = counts.Failed;
            run.ItemsDead = counts.Dead;
            run.ItemsSkipped = counts.Skipped;
            run.CostMicros = counts.CostMicros;
        }

        private async Task<bool> MonthlyCapReachedAsync(DateTime now, CancellationToken token)
        {
            long cap = Micros(_Services.Settings.Jobs.MonthlyBudgetUsd);
            long spent = await _Services.Database.Jobs.CostMicrosSinceAsync(MonthStart(now), token).ConfigureAwait(false);
            return spent >= cap;
        }

        private static DateTime MonthStart(DateTime now)
        {
            return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        private static long Micros(double usd)
        {
            return (long)Math.Round(usd * 1000000d, MidpointRounding.AwayFromZero);
        }

        private static double Usd(long micros)
        {
            return Math.Round(micros / 1000000d, 6);
        }

        private static string Truncate(string text, int max)
        {
            return text.Length <= max ? text : text.Substring(0, max);
        }

        private JobDefinitionView View(JobDefinition d, JobRun? latest)
        {
            IJobHandler? handler = Registry.Find(d.Key);
            return new JobDefinitionView
            {
                Id = d.Id,
                Key = d.Key,
                Name = d.Name,
                Description = d.Description,
                Enabled = d.Enabled,
                IntervalType = d.IntervalType.ToString(),
                IntervalValue = d.IntervalValue,
                TimeOfDayMinutesUtc = d.TimeOfDayMinutesUtc,
                DaysOfWeek = d.DaysOfWeek.Select(x => x.ToString()).ToList(),
                MaxConcurrency = d.MaxConcurrency,
                MaxAttempts = d.MaxAttempts,
                BudgetUsdPerRun = d.BudgetMicrosPerRun == null ? null : Usd(d.BudgetMicrosPerRun.Value),
                RequiresHarness = d.RequiresHarness,
                SupportsDryRun = handler?.Defaults.SupportsDryRun ?? false,
                HandlerAvailable = handler != null,
                NextRunUtc = d.NextRunUtc,
                LastRunUtc = d.LastRunUtc,
                LatestRun = latest == null ? null : RunView(latest, null),
                CreatedUtc = d.CreatedUtc,
                UpdatedUtc = d.UpdatedUtc
            };
        }

        private static JobRunView RunView(JobRun r, JobItemCounts? counts)
        {
            return new JobRunView
            {
                Id = r.Id,
                Key = r.Key,
                Trigger = r.Trigger.ToString(),
                Status = r.Status.ToString(),
                IsDryRun = r.IsDryRun,
                SkippedReason = r.SkippedReason,
                Error = r.Error,
                RequestedByUserId = r.RequestedByUserId,
                CreatedUtc = r.CreatedUtc,
                StartedUtc = r.StartedUtc,
                CompletedUtc = r.CompletedUtc,
                ItemsTotal = counts == null ? r.ItemsTotal : Math.Max(r.ItemsTotal, counts.Total),
                ItemsOpen = counts?.Open ?? Math.Max(0, r.ItemsTotal - r.ItemsSucceeded - r.ItemsFailed - r.ItemsDead - r.ItemsSkipped),
                ItemsSucceeded = counts?.Succeeded ?? r.ItemsSucceeded,
                ItemsFailed = counts?.Failed ?? r.ItemsFailed,
                ItemsDead = counts?.Dead ?? r.ItemsDead,
                ItemsSkipped = counts?.Skipped ?? r.ItemsSkipped,
                CostUsd = Usd(counts?.CostMicros ?? r.CostMicros),
                Parameters = r.Parameters,
                Summary = r.Summary
            };
        }

        private static JobItemView ItemView(JobItem i)
        {
            return new JobItemView
            {
                Id = i.Id,
                RunId = i.RunId,
                Key = i.Key,
                TargetKey = i.TargetKey,
                Payload = i.Payload,
                Status = i.Status.ToString(),
                Attempts = i.Attempts,
                MaxAttempts = i.MaxAttempts,
                NextAttemptUtc = i.NextAttemptUtc,
                RequiresHarness = i.RequiresHarness,
                Result = i.Result,
                Error = i.Error,
                CostUsd = Usd(i.CostMicros),
                CreatedUtc = i.CreatedUtc,
                StartedUtc = i.StartedUtc,
                CompletedUtc = i.CompletedUtc
            };
        }

        #endregion
    }
}
