namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin job engine routes (docs/INTEGRATION.md): definitions with schedules and budgets, run now and dry
    /// run, run history with live counts, cancel, per-item inspection, the dead-letter view, and retry. SystemAdmin or
    /// the admin token.
    /// </summary>
    public class AdminJobRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/jobs";
        private const string _Tag = "Admin: jobs";
        private const string _Admin = " SystemAdmin or the admin token.";
        private const string _Definition = "JobDefinition { id, key, name, description, enabled, intervalType, intervalValue, timeOfDayMinutesUtc, daysOfWeek, maxConcurrency, maxAttempts, budgetUsdPerRun, requiresHarness, supportsDryRun, handlerAvailable, nextRunUtc, lastRunUtc, latestRun, createdUtc, updatedUtc, labels, tags }";
        private const string _Run = "JobRun { id, key, trigger, status, isDryRun, skippedReason, error, requestedByUserId, createdUtc, startedUtc, completedUtc, itemsTotal, itemsOpen, itemsSucceeded, itemsFailed, itemsDead, itemsSkipped, costUsd, parameters, summary, labels, tags }";
        private const string _Item = "JobItem { id, runId, key, targetKey, payload, status, attempts, maxAttempts, nextAttemptUtc, requiresHarness, result, error, costUsd, createdUtc, startedUtc, completedUtc, labels, tags }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">directory is null.</exception>
        public AdminJobRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the routes.
        /// </summary>
        /// <param name="server">Webserver.</param>
        /// <exception cref="ArgumentNullException">server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;
            EnumerationRoutes.Add(post, _Base, JobCollections.AdminDefinitions, _Definition,
                () => Doc("Enumerate jobs", _Tag, "Paged definitions with schedules, budgets, latest runs, and metadata. Search key, name, and description." + _Admin),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Jobs.EnumerateAsync(actor, query, ctx.Token)));
            post.Static.Add(HttpMethod.GET, _Base + "/overview", OverviewAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Job overview", _Tag, "The month's job cost against jobs.monthlyBudgetUsd, open runs, and definition counts." + _Admin)
                    .Returns("JobOverview { enabled, runsHarnesses, monthStartUtc, monthCostUsd, monthlyBudgetUsd, remainingUsd, openRuns, definitions, enabledDefinitions }").Resp(403, "Forbidden"));
            EnumerationRoutes.Add(post, _Base + "/runs", JobCollections.AdminRuns, _Run,
                () => Doc("List job runs", _Tag, "Newest first. Filters key, status (Queued, Running, Succeeded, PartiallySucceeded, Failed, Cancelled, Skipped), trigger (Schedule, Manual, DryRun)." + _Admin),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Jobs.EnumerateRunsAsync(actor, query, ctx.Token)));
            EnumerationRoutes.Add(post, _Base + "/items", JobCollections.AdminItems, _Item,
                () => Doc("List job items", _Tag, "Newest first. Filters runId, key, status (Pending, Running, Succeeded, Failed, Dead, Skipped); status Dead is the dead-letter view. Search over target key and error." + _Admin),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Jobs.EnumerateItemsAsync(actor, query, null, ctx.Token)));
            post.Parameter.Add(HttpMethod.GET, _Base + "/runs/{id}", GetRunAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a job run", _Tag, "With live item counts and cost." + _Admin).PathParam("id", "Run id").Returns(_Run).Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/runs/{id}/cancel", CancelAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Cancel a job run", _Tag, "Pending items are skipped; items already running finish. Audited as job.cancel." + _Admin)
                    .PathParam("id", "Run id").Returns(_Run).Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "RunFinished"));
            EnumerationRoutes.Add(post, _Base + "/runs/{id}/items", JobCollections.AdminItems, _Item,
                () => Doc("List the items of a job run", _Tag, "Newest first. Filters status and key." + _Admin),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Jobs.EnumerateItemsAsync(actor, query, RouteHelper.Param(ctx, "id"), ctx.Token)), "id");
            post.Parameter.Add(HttpMethod.POST, _Base + "/items/{id}/retry", RetryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Retry a job item", _Tag, "A dead or failed item returns to Pending with its attempts reset, and its run reopens. Audited as job.item.retry." + _Admin)
                    .PathParam("id", "Item id").Returns(_Item).Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "NotRetryable"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{key}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a job", _Tag, _Admin.Trim()).PathParam("key", "Job key (for example jobs.prune)").Returns(_Definition).Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{key}", UpdateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Change a job", _Tag, "Body { enabled?, intervalType?, intervalValue?, timeOfDayMinutesUtc?, daysOfWeek?, maxConcurrency?, maxAttempts?, budgetUsdPerRun? (0 removes the cap) }. The next run is recomputed. Audited as job.update." + _Admin)
                    .PathParam("key", "Job key").Body("JobDefinitionRequest").Returns(_Definition).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{key}/run", RunNowAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Run a job now", _Tag, "Body { dryRun?, parameters? }. Parameters is a JSON object encoded as a string, at most 16384 characters, validated by the handler. Queues a run whether or not the schedule is enabled. Audited as job.run." + _Admin)
                    .PathParam("key", "Job key").Body("JobRunRequest").Returns(_Run).Resp(400, "DryRunNotSupported").Resp(400, "InvalidJobParameters").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "RunActive").Resp(409, "BudgetExceeded").Resp(409, "HandlerMissing"));
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private Task<ContentActor> ActorAsync(HttpContextBase ctx)
        {
            return _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token);
        }

        private async Task<object> RunAsync<T>(HttpContextBase ctx, Func<ContentActor, Task<T>> body) where T : class
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            return await body(actor).ConfigureAwait(false);
        }

        private async Task OverviewAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Jobs.OverviewAsync(actor, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Jobs.GetAsync(actor, RouteHelper.Param(ctx, "key"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            JobDefinitionRequest request = RouteHelper.ReadJson<JobDefinitionRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Jobs.UpdateAsync(actor, Info(ctx), RouteHelper.Param(ctx, "key"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RunNowAsync(HttpContextBase ctx)
        {
            JobRunRequest request = RouteHelper.ReadJson<JobRunRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Jobs.RunNowAsync(actor, Info(ctx), RouteHelper.Param(ctx, "key"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetRunAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Jobs.GetRunAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CancelAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Jobs.CancelAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RetryAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Jobs.RetryItemAsync(actor, Info(ctx), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
