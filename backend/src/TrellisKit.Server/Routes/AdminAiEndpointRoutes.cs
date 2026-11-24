namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin model endpoint routes (docs/AI_CONTRACT.md section 1.3). SystemAdmin or the admin token; 404 NotAvailable
    /// unless AI is on for the property. Request bodies of these routes are never captured in request history.
    /// </summary>
    public class AdminAiEndpointRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/ai/endpoints";
        private const string _Tag = "Admin: AI endpoints";
        private const string _Admin = " SystemAdmin or the admin token.";
        private const string _Orderings = "NameAscending, CreatedDescending, CreatedAscending, UpdatedDescending, HealthStatus";
        private const string _Filters = "kind, apiType, enabled, healthStatus, authType, scope";
        private const string _Body = "{ name, description?, enabled?, kind, apiType, model, endpointUrl?, authType?, authHeaderName?, authQueryParameter?, authUsername?, authValue? (write-only), extraHeaders?: [{ name, value? }], providerSettings? { region?, project?, apiVersion?, deployment?, accessKeyId? }, parameters?, limits?, healthCheck?, pricing?: { inputPerMillionTokens, outputPerMillionTokens }, labels?, tags? }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminAiEndpointRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">The server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            WatsonWebserver.Core.Routing.RoutingGroup post = server.Routes.PostAuthentication;
            post.Static.Add(HttpMethod.GET, _Base, ListAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List model endpoints", _Tag, "Secrets are never returned: hasAuthValue and extraHeaders[].hasValue say whether they are set." + _Admin)
                    .Enumeration("ModelEndpoint", _Orderings, _Filters).Resp(400, "ValidationFailed").Resp(404, "NotAvailable"));
            post.Static.Add(HttpMethod.POST, _Base + "/enumerate", EnumerateAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate model endpoints", _Tag, "EnumerationQuery body." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration("ModelEndpoint", _Orderings, _Filters, false).Resp(400, "ValidationFailed").Resp(404, "NotAvailable"));
            post.Static.Add(HttpMethod.POST, _Base, CreateAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a model endpoint", _Tag, "Body " + _Body + ". apiType Ollama, OpenAI, OpenAICompatible, Gemini, AzureOpenAI, Anthropic, Bedrock, VertexAI, OllamaCloud, XAI, Cohere, VoyageAI, or Tei (limited by ai.enabledApiTypes). " +
                    "providerSettings: AzureOpenAI needs deployment (apiVersion optional; endpointUrl is the resource endpoint; auth ApiKey or Bearer), VertexAI needs project and region (auth ApiKey with the service account JSON, or None for application default credentials), " +
                    "Bedrock needs region and, with ApiKey auth, accessKeyId (authValue is the secret access key; None uses the environment credentials); every other field is null. Secrets are encrypted at rest with secrets.encryptionKey." + _Admin)
                    .Body("ModelEndpointRequest").Returns("ModelEndpoint", 201).Resp(400, "ValidationFailed or AuthTypeNotSupported").Resp(409, "Conflict (name)"));
            post.Static.Add(HttpMethod.POST, _Base + "/validate", ValidateAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Validate and test an unsaved endpoint", _Tag, "A create body; nothing is stored. Rate limited per admin." + _Admin)
                    .Body(_Body).Returns("TestResult").Resp(400, "ValidationFailed or AuthTypeNotSupported").Resp(429, "RateLimited"));
            post.Static.Add(HttpMethod.POST, _Base + "/reencrypt", ReencryptAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Re-encrypt stored secrets", _Tag, "Rewrites every secret stored with a previous key using secrets.encryptionKey. Audited." + _Admin)
                    .Returns("{ endpoints, secrets, keyId }").Resp(400, "ValidationFailed (no key)"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}", GetAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a model endpoint", _Tag, "With usedBy (at most 10 references and the enumerate path)." + _Admin)
                    .PathParam("id", "Endpoint identifier").Returns("ModelEndpoint").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{id}", UpdateAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update a model endpoint", _Tag, "Partial. Nested objects merge field by field; null resets a field. authValue omitted keeps it, a value replaces it, empty or null clears it; extraHeaders replaces the list and an entry without value keeps the stored value of the same name." + _Admin)
                    .PathParam("id", "Endpoint identifier").Body(_Body).Returns("ModelEndpoint").Resp(400, "ValidationFailed or AuthTypeNotSupported").Resp(404, "NotFound").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/{id}", DeleteAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a model endpoint", _Tag, "Health history and usage rollups are kept." + _Admin)
                    .PathParam("id", "Endpoint identifier").Resp(204, "Deleted").Resp(404, "NotFound").Resp(409, "EndpointInUse (fieldErrors.assistants lists up to 10 ids)"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/test", TestAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Test a model endpoint", _Tag, "Body { prompt?, maxTokens?, input?, query?, documents? }. Sends a short prompt, embeds input, or reranks documents by kind and reports latency, time to first token, and output (failures are 200 with success false and a sanitized error). Audited; rate limited." + _Admin)
                    .PathParam("id", "Endpoint identifier").Body("EndpointTestRequest", false).Returns("TestResult").Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/models/enumerate", ModelsAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate provider models", _Tag, "From the provider's model listing, cached for ai.modelListCacheMinutes (at most ai.maxListedModels). Search over name." + _Admin)
                    .PathParam("id", "Endpoint identifier").Body("EnumerationQuery", false).Enumeration("ProviderModel", "NameAscending", "", false).Resp(404, "NotFound").Resp(502, "ModelError").Resp(503, "ModelUnavailable"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{id}/health", HealthAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read endpoint health", _Tag, "{ health, recent: { objects (at most 20), totalRecords, enumerate } }." + _Admin)
                    .PathParam("id", "Endpoint identifier").Returns("EndpointHealth").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/health/enumerate", HealthEnumerateAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate endpoint health checks", _Tag, "Newest first; the created window applies to the check time; filter success." + _Admin)
                    .PathParam("id", "Endpoint identifier").Body("EnumerationQuery", false).Enumeration("HealthCheckRecord", "CreatedDescending, CreatedAscending", "success", false).Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/health/check", HealthCheckAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Check endpoint health now", _Tag, "Runs a check and updates the status. Rate limited." + _Admin)
                    .PathParam("id", "Endpoint identifier").Returns("HealthCheckRecord").Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/duplicate", DuplicateAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Duplicate a model endpoint", _Tag, "Body { name?, includeSecrets? }. The copy is disabled with health Unknown. Audited." + _Admin)
                    .PathParam("id", "Endpoint identifier").Body("EndpointDuplicateRequest", false).Returns("ModelEndpoint", 201).Resp(404, "NotFound").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/reveal", RevealAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reveal a stored secret", _Tag, "Body { field: authValue | extraHeader, headerName?, reason }. Needs a SystemAdmin member session signed in within ai.revealRecentSignInMinutes (never the admin token) and a reason; audited without the value; Cache-Control no-store; rate limited; off when ai.allowSecretReveal is false.")
                    .PathParam("id", "Endpoint identifier").Body("SecretRevealRequest").Returns("{ field, headerName, value }")
                    .Resp(400, "ValidationFailed").Resp(403, "RevealNotAllowed").Resp(404, "NotFound").Resp(429, "RateLimited"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{id}/usage/enumerate", UsageAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate endpoint references", _Tag, "{ assistantId, assistantName, purpose } per assistant purpose." + _Admin)
                    .PathParam("id", "Endpoint identifier").Body("EnumerationQuery", false).Enumeration("EndpointUsage", "NameAscending", "", false).Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Endpoints.EnumerateAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task EnumerateAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Endpoints.EnumerateAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            ModelEndpointRequest request = RouteHelper.ReadJson<ModelEndpointRequest>(ctx);
            ApplicationServices s = _Directory.For(ctx);
            ContentActor actor = await s.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await s.Ai.Endpoints.CreateAsync(actor, request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ValidateAsync(HttpContextBase ctx)
        {
            ModelEndpointRequest request = RouteHelper.ReadJson<ModelEndpointRequest>(ctx);
            ApplicationServices s = _Directory.For(ctx);
            ContentActor actor = await s.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Endpoints.ValidateAsync(actor, request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ReencryptAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Endpoints.ReencryptAsync(Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Endpoints.GetAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            ModelEndpointRequest request = RouteHelper.ReadJson<ModelEndpointRequest>(ctx, out HashSet<string> present);
            ApplicationServices s = _Directory.For(ctx);
            ContentActor actor = await s.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Endpoints.UpdateAsync(actor, RouteHelper.Param(ctx, "id"), request, present, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            await _Directory.For(ctx).Ai.Endpoints.DeleteAsync(RouteHelper.Param(ctx, "id"), Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task TestAsync(HttpContextBase ctx)
        {
            EndpointTestRequest request = RouteHelper.ReadJson<EndpointTestRequest>(ctx);
            ApplicationServices s = _Directory.For(ctx);
            ContentActor actor = await s.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Endpoints.TestAsync(actor, RouteHelper.Param(ctx, "id"), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ModelsAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            ApplicationServices s = _Directory.For(ctx);
            ContentActor actor = await s.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Endpoints.EnumerateModelsAsync(actor, RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task HealthAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Health.DetailAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task HealthEnumerateAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Health.EnumerateAsync(RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task HealthCheckAsync(HttpContextBase ctx)
        {
            ApplicationServices s = _Directory.For(ctx);
            ContentActor actor = await s.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Health.CheckNowAsync(actor, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DuplicateAsync(HttpContextBase ctx)
        {
            EndpointDuplicateRequest request = RouteHelper.ReadJson<EndpointDuplicateRequest>(ctx);
            ApplicationServices s = _Directory.For(ctx);
            ContentActor actor = await s.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await s.Ai.Endpoints.DuplicateAsync(actor, RouteHelper.Param(ctx, "id"), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RevealAsync(HttpContextBase ctx)
        {
            SecretRevealRequest request = RouteHelper.ReadJson<SecretRevealRequest>(ctx);
            ApplicationServices s = _Directory.For(ctx);
            ContentActor actor = await s.Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["Cache-Control"] = "no-store";
            await RouteHelper.SendJsonAsync(ctx, 200, await s.Ai.Endpoints.RevealAsync(actor, RouteHelper.Param(ctx, "id"), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UsageAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.Endpoints.EnumerateUsageAsync(RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
