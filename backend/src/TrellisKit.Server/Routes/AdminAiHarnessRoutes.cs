namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin harness profile routes (docs/HARNESSES.md, "Harness profiles"): the model, permission mode, limits, base
    /// URL, and credential of each CLI agent harness runtime. SystemAdmin or the admin token; the reveal route needs a
    /// member session.
    /// </summary>
    public class AdminAiHarnessRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/ai/harnesses";
        private const string _Tag = "Admin: AI harness profiles";
        private const string _Admin = " SystemAdmin or the admin token.";
        private const string _Profile = "AiHarnessProfile { runtime, supportsJobTools, credentialTypes, baseUrlVariable, credentialVariables, credentialVariablesByType, environmentVariables, model, permissionMode, timeoutSeconds, maxToolCalls, allowWebResearch, baseUrl, credentialType, hasCredential, defaultTimeoutSeconds, defaultMaxToolCalls, runsOnThisInstance, saved, updatedByUserId, updatedUtc }";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">directory is null.</exception>
        public AdminAiHarnessRoutes(PropertyDirectory directory)
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
            EnumerationRoutes.Add(post, _Base, AiCollections.AdminHarnesses, "AiHarnessProfile",
                () => Doc("Enumerate AiHarnessProfile catalog", _Tag, "Standard catalog enumeration. Immutable catalog entries have createdUtc at the Unix epoch. Search matches catalog keys and descriptive text." + _Admin),
                async (ctx, query) => await _Directory.For(ctx).Ai.HarnessProfiles.EnumerateAsync(await ActorAsync(ctx).ConfigureAwait(false), query, ctx.Token).ConfigureAwait(false));
            post.Parameter.Add(HttpMethod.GET, _Base + "/{runtime}", GetAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a harness profile", _Tag, _Admin.Trim()).PathParam("runtime", "Harness runtime (for example ClaudeCode)").Returns(_Profile).Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/{runtime}", SaveAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Change a harness profile", _Tag, "Body { model?, permissionMode? (Restricted or ReadOnly), timeoutSeconds?, maxToolCalls?, allowWebResearch?, baseUrl?, credentialType? (Environment, ApiKey, AuthToken, OAuthToken), credential? }. The profile is replaced; the credential is write-only (omit to keep, empty to clear) and stored encrypted. Audited as ai.harness.update." + _Admin)
                    .PathParam("runtime", "Harness runtime").Body("AiHarnessProfileRequest").Returns(_Profile).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{runtime}/reveal", RevealAsync, AiRouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Reveal a harness credential", _Tag, "Body { reason }. Needs a SystemAdmin member session signed in within ai.revealRecentSignInMinutes (never the admin token) and a reason; audited without the value; Cache-Control no-store; rate limited; off when ai.allowSecretReveal is false.")
                    .PathParam("runtime", "Harness runtime").Body("HarnessRevealRequest").Returns("{ field, headerName, value }")
                    .Resp(400, "ValidationFailed").Resp(403, "RevealNotAllowed").Resp(404, "NotFound").Resp(429, "RateLimited"));
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

        private async Task GetAsync(HttpContextBase ctx)
        {
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.HarnessProfiles.GetAsync(actor, RouteHelper.Param(ctx, "runtime"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SaveAsync(HttpContextBase ctx)
        {
            AiHarnessProfileRequest request = RouteHelper.ReadJson<AiHarnessProfileRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.HarnessProfiles.SaveAsync(actor, Info(ctx), RouteHelper.Param(ctx, "runtime"), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RevealAsync(HttpContextBase ctx)
        {
            HarnessRevealRequest request = RouteHelper.ReadJson<HarnessRevealRequest>(ctx);
            ContentActor actor = await ActorAsync(ctx).ConfigureAwait(false);
            ctx.Response.Headers["Cache-Control"] = "no-store";
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).Ai.HarnessProfiles.RevealAsync(actor, Info(ctx), RouteHelper.Param(ctx, "runtime"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
