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
    /// Admin AI task role routes (docs/INTEGRATION.md): executor hierarchies per role, a test
    /// prompt through a hierarchy, and the per-call ledger. SystemAdmin or the admin token.
    /// </summary>
    public class AdminAiRoleRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/ai/roles";
        private const string _Calls = Constants.ApiPrefix + "/admin/ai/calls";
        private const string _Tag = "Admin: AI task roles";
        private const string _Admin = " SystemAdmin or the admin token.";
        private const string _Role = "AiTaskRole { role, title, description, kind, allowsHarness, interactive, suggested, reasoningEffort, availabilityPolicy, exhaustionPolicy, executors [{ position, runtime, endpointId, endpointName, model, health, enabled, problem }], ready, updatedByUserId, updatedUtc }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">directory is null.</exception>
        public AdminAiRoleRoutes(PropertyDirectory directory)
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
            EnumerationRoutes.Add(post, _Base, AiCollections.AdminRoles, "AiTaskRole",
                () => Doc("Enumerate AiTaskRole catalog", _Tag, "Standard catalog enumeration. Immutable catalog entries have createdUtc at the Unix epoch. Search matches catalog keys and descriptive text." + _Admin),
                async (ctx, query) => await _Directory.For(ctx).Ai.Roles.EnumerateAsync(await ActorAsync(ctx).ConfigureAwait(false), query, ctx.Token).ConfigureAwait(false));
            EnumerationRoutes.Add(post, _Calls, AiCollections.AdminCalls, "AiCall { id, createdUtc, purpose, role, assistantId, endpointId, runtime, model, executorPosition, escalationReason, outcome, errorCode, isFallback, isTest, promptTokens, completionTokens, reasoningTokens, inputTokens, outputTokens, totalTokens, cachedReadTokens, cacheCreationTokens, inputTextTokens, inputImageTokens, inputAudioTokens, inputVideoTokens, latencyMs, queueMs, timeToFirstTokenMs, timeToLastTokenMs, streamingMs, tokensPerSecond, loadMs, promptEvalMs, evalMs, toolCalls, toolExecMs, retries, statusCode, finishReason, streamed, chunks, requestBytes, responseBytes, responseId, providerModel, reasoningEffort, exitCode, costUsd, jobKey, jobRunId, jobItemId, labels, tags }",
                () => Doc("List AI calls", _Tag, "The per-call ledger, newest first, with every call's telemetry (null where the provider reports none). Filters role, purpose, outcome, endpointId, jobKey, jobRunId, assistantId." + _Admin),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Ai.Roles.EnumerateCallsAsync(actor, query, ctx.Token)));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{role}", GetAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read an AI task role", _Tag, _Admin.Trim()).PathParam("role", "Role key (for example Extraction)").Returns(_Role).Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{role}", SaveAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Change an AI task role", _Tag, "Body { reasoningEffort?, availabilityPolicy?, exhaustionPolicy?, executors: [{ runtime, endpointId?, model?, enabled? }] } (at most 8; harness runtimes only on agentic background roles). Audited as ai.role.update." + _Admin)
                    .PathParam("role", "Role key").Body("AiTaskRoleRequest").Returns(_Role).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{role}/test", TestAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Test an AI task role", _Tag, "Body { prompt }. Walks the hierarchy as a real call would (recorded as test traffic) and reports which executor answered and why earlier ones were passed over." + _Admin)
                    .PathParam("role", "Role key").Body("AiRoleTestRequest").Returns("AiRoleTestResult { success, text, executorPosition, endpointId, model, escalations, errorCode, latencyMs, costUsd }")
                    .Resp(400, "ValidationFailed").Resp(400, "NotTestable").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(404, "NotAvailable"));
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

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Roles.GetAsync(actor, RouteHelper.Param(ctx, "role"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SaveAsync(HttpContextBase ctx)
        {
            AiTaskRoleRequest request = RouteHelper.ReadJson<AiTaskRoleRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Roles.SaveAsync(actor, Info(ctx), RouteHelper.Param(ctx, "role"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task TestAsync(HttpContextBase ctx)
        {
            AiRoleTestRequest request = RouteHelper.ReadJson<AiRoleTestRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Roles.TestAsync(actor, RouteHelper.Param(ctx, "role"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
