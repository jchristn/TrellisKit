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
    /// Admin AI prompt routes (docs/INTEGRATION.md owner requirement R7): every prompt of the engine is listed, edited as
    /// a draft, benchmarked, published through the scope benchmark gate, kept with version history, and reset to its
    /// default here. SystemAdmin or the admin token.
    /// </summary>
    public class AdminAiPromptRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/ai/prompts";
        private const string _Tag = "Admin: AI prompts";
        private const string _Admin = " SystemAdmin or the admin token.";
        private const string _Prompt = "AiPrompt { key, title, description, content, defaultContent, defaultSource (Engine, Property), isDefault, version, draft (AiPromptVersion or null), draftBenchmark (JobRun or null), requiresBenchmark, minLength, maxLength, variables, requiredWords, lockedPrefix, updatedByUserId, updatedUtc }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">directory is null.</exception>
        public AdminAiPromptRoutes(PropertyDirectory directory)
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
            EnumerationRoutes.Add(post, _Base, AiCollections.AdminPrompts, "AiPrompt",
                () => Doc("Enumerate AiPrompt catalog", _Tag, "Standard catalog enumeration. Immutable catalog entries have createdUtc at the Unix epoch. Search matches catalog keys and descriptive text." + _Admin),
                async (ctx, query) => await _Directory.For(ctx).Ai.Prompts.EnumerateAsync(await ActorAsync(ctx).ConfigureAwait(false), query, ctx.Token).ConfigureAwait(false));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{key}", GetAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read an AI prompt", _Tag, _Admin.Trim()).PathParam("key", "Prompt key").Returns(_Prompt).Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{key}", SetAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Save an AI prompt draft", _Tag, "Body { content } within the prompt's length limits, containing its required words, and without code. Saves a draft version; no model call uses it until it is published. Audited as ai.prompt.update." + _Admin)
                    .PathParam("key", "Prompt key").Body("AiPromptRequest").Returns(_Prompt).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{key}/publish", PublishAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Publish an AI prompt draft", _Tag, "Body { versionId? }. Makes the pending draft live after the deterministic scope gate passes and, when ai.promptPublishRequiresBenchmark is on, the newest live ai.scopeBenchmark run of that draft is complete, passes its thresholds, used the current benchmark set, and measured the other prompts at their current versions. Audited as ai.prompt.publish." + _Admin)
                    .PathParam("key", "Prompt key").Body("AiPromptPublishRequest").Returns(_Prompt).Resp(403, "Forbidden").Resp(404, "NotFound")
                    .Resp(409, "NoDraft, DraftChanged, ScopeGateFailed, BenchmarkRequired, BenchmarkRunning, BenchmarkFailed, or BenchmarkStale"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{key}/benchmark", BenchmarkAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Benchmark an AI prompt draft", _Tag, "Body { dryRun?, assistantId?, replyRole? }. Queues the ai.scopeBenchmark job for the prompt's pending draft (the prompts in use when there is none); returns 201 with the run, whose summary holds the pass rates when it finishes. Audited as job.run." + _Admin)
                    .PathParam("key", "Prompt key").Body("AiPromptBenchmarkRequest").Returns("JobRun").Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "RunActive or BudgetExceeded"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{key}/reset", ResetAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reset an AI prompt", _Tag, "Returns to the property default (ai/prompts/<key>.md) or the engine default, recorded as a version. Audited as ai.prompt.reset." + _Admin)
                    .PathParam("key", "Prompt key").Returns(_Prompt).Resp(403, "Forbidden").Resp(404, "NotFound"));
            EnumerationRoutes.Add(post, _Base + "/{key}/versions", AiCollections.AdminPromptVersions, "AiPromptVersion { id, key, version, content, isReset, createdByUserId, createdUtc, labels, tags }",
                () => Doc("List AI prompt versions", _Tag, "Every saved version of one prompt, newest first." + _Admin),
                (ctx, query) => RunAsync(ctx, actor => _Directory.For(ctx).Ai.Prompts.VersionsAsync(actor, RouteHelper.Param(ctx, "key"), query, ctx.Token)), "key");
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
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Prompts.GetViewAsync(actor, RouteHelper.Param(ctx, "key"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SetAsync(HttpContextBase ctx)
        {
            AiPromptRequest request = RouteHelper.ReadJson<AiPromptRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Prompts.SetAsync(actor, Info(ctx), RouteHelper.Param(ctx, "key"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PublishAsync(HttpContextBase ctx)
        {
            AiPromptPublishRequest? request = RouteHelper.ReadJson<AiPromptPublishRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Prompts.PublishAsync(actor, Info(ctx), RouteHelper.Param(ctx, "key"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task BenchmarkAsync(HttpContextBase ctx)
        {
            AiPromptBenchmarkRequest? request = RouteHelper.ReadJson<AiPromptBenchmarkRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await _Directory.For(ctx).Ai.Prompts.BenchmarkAsync(actor, Info(ctx), RouteHelper.Param(ctx, "key"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ResetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Prompts.ResetAsync(actor, Info(ctx), RouteHelper.Param(ctx, "key"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
