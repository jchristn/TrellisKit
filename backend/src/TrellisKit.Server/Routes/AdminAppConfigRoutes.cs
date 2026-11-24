namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin app configuration routes (docs/LIVE_UPDATES_CONTRACT.md sections 12.4, 13.3, and 16): feature flags (drafts,
    /// Test as, history), content publishing and versions with restore, and live kill switches. SystemAdmin, ReleaseManager,
    /// or the admin token.
    /// </summary>
    public class AdminAppConfigRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/app";
        private const string _Tag = "Admin: app configuration";
        private const string _Admin = " SystemAdmin, ReleaseManager, or the admin token.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminAppConfigRoutes(PropertyDirectory directory)
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
            const string flagItem = "FeatureFlag";
            post.Static.Add(HttpMethod.POST, _Base + "/flags/enumerate", FlagsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate feature flags", _Tag, "The draft of each flag when one is pending, else its published definition. Filters kind, state, hasDraftChanges, client; orderings KeyAscending (default), UpdatedDescending, CreatedDescending, CreatedAscending; search over key, name, description." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration(flagItem, "KeyAscending, UpdatedDescending, CreatedDescending, CreatedAscending", "kind, state, hasDraftChanges, client", false).Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, _Base + "/flags", CreateFlagAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a feature flag", _Tag, "{ key, name, description?, kind: Boolean | Variant | Value, valueType?, variants?, defaultValue, offValue, enabled?, clients?, rules?, labels?, tags? } as a draft; it reaches clients at the next content publish." + _Admin)
                    .Body("FeatureFlag").Returns(flagItem, 201).Resp(400, "ValidationFailed or InvalidTargetingRule").Resp(409, "FlagKeyInUse"));
            post.Parameter.Add(HttpMethod.GET, _Base + "/flags/{id}", GetFlagAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a feature flag", _Tag, "{ flag (draft or published), published, history { objects (max 10), totalRecords, enumerate } }; ETag header for If-Match." + _Admin)
                    .PathParam("id", "Flag id").Bounded("FeatureFlagDetail", "rules at most appConfig.maxRulesPerFlag, history preview at most 10 with totalRecords and the enumerate path").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/flags/{id}", UpdateFlagAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update a feature flag draft", _Tag, "Absent fields are unchanged; rules replace the whole list; the key cannot change after the first publish. If-Match required." + _Admin)
                    .PathParam("id", "Flag id").Body("Partial FeatureFlag").Returns(flagItem).Resp(400, "ValidationFailed or InvalidTargetingRule").Resp(404, "NotFound").Resp(409, "FlagKeyInUse").Resp(412, "PreconditionFailed"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/flags/{id}/archive", ArchiveAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Archive a feature flag", _Tag, "Draft change: removed from manifests at the next publish; the key stays reserved." + _Admin).PathParam("id", "Flag id").Body("Body", false).Returns(flagItem).Resp(404, "NotFound").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/flags/{id}/restore", RestoreFlagAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Restore an archived flag", _Tag, "Draft change." + _Admin).PathParam("id", "Flag id").Body("Body", false).Returns(flagItem).Resp(404, "NotFound").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/flags/{id}", DeleteFlagAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a feature flag", _Tag, "Only flags that were never published." + _Admin).PathParam("id", "Flag id").Resp(204, "Deleted").Resp(404, "NotFound").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/flags/{id}/evaluate", EvaluateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Test a flag as a caller", _Tag, "Body { as, useDraft }. { as: { platform, client, appVersion, updateId, runtimeVersion, channel, signedIn, userId, installId, country }, useDraft } returns { value, matchedRuleId, reason, bucket }." + _Admin)
                    .PathParam("id", "Flag id").Body("FlagEvaluateRequest").Returns("FlagEvaluation").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/flags/{id}/history/enumerate", HistoryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate a flag's published revisions", _Tag, "Newest first (VersionDescending)." + _Admin)
                    .PathParam("id", "Flag id").Body("EnumerationQuery", false).Enumeration(flagItem, "VersionDescending, CreatedDescending, CreatedAscending", String.Empty, false).Resp(404, "NotFound"));
            post.Static.Add(HttpMethod.GET, _Base + "/drafts", DraftsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Pending content drafts", _Tag, "{ contentVersion, objects (max 100), totalRecords, enumerate }; send contentVersion as expectedVersion when publishing." + _Admin)
                    .Enumeration("ContentDraft", "CreatedDescending, CreatedAscending", ""));
            post.Static.Add(HttpMethod.POST, _Base + "/drafts/enumerate", EnumerateDraftsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate content drafts", _Tag, "Ordering KeyAscending (default)." + _Admin).Body("EnumerationQuery", false).Enumeration(flagItem, "KeyAscending, CreatedDescending, CreatedAscending", String.Empty, false));
            post.Static.Add(HttpMethod.POST, _Base + "/drafts/discard", DiscardAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Discard content drafts", _Tag, "Body { objectIds? }. { objectIds? }: all drafts when omitted." + _Admin).Body("DraftDiscardRequest", false).Returns("{ discarded }"));
            post.Static.Add(HttpMethod.POST, _Base + "/publish", PublishAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Publish content", _Tag, "Body { message, expectedVersion }. { message, expectedVersion }: every pending draft becomes the next content version atomically." + _Admin)
                    .Body("ContentPublishRequest").Returns("ContentVersion", 201).Resp(400, "ValidationFailed").Resp(409, "ContentVersionConflict"));
            post.Static.Add(HttpMethod.POST, _Base + "/versions/enumerate", VersionsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate content versions", _Tag, "Ordering VersionDescending (default); search over message." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration("ContentVersion", "VersionDescending, CreatedDescending, CreatedAscending", String.Empty, false));
            post.Parameter.Add(HttpMethod.GET, _Base + "/versions/{version}", VersionAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a content version", _Tag, "With changes { objects (max 50), totalRecords, enumerate }." + _Admin)
                    .PathParam("version", "Version number").Bounded("ContentVersion", "changes preview at most 50 with totalRecords and the enumerate path").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/versions/{version}/changes/enumerate", ChangesAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate a version's changes", _Tag, "The flags as published in that version." + _Admin)
                    .PathParam("version", "Version number").Body("EnumerationQuery", false).Enumeration(flagItem, "KeyAscending, CreatedDescending, CreatedAscending", String.Empty, false).Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/versions/{version}/restore", RestoreVersionAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Restore a content version", _Tag, "Body { message? }. { message? }: publishes a new version whose flags equal their state at that version (kill switches untouched)." + _Admin)
                    .PathParam("version", "Version number").Body("ContentPublishRequest", false).Returns("ContentVersion", 201).Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(409, "ContentVersionConflict"));
            post.Static.Add(HttpMethod.POST, _Base + "/kill-switches/enumerate", KillSwitchesAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate kill switches", _Tag, "One per engine feature key: killed, killedForEveryone, message, rules, last change and reason. Filter killed." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration("KillSwitch", "KeyAscending, CreatedDescending, CreatedAscending", "killed", false).Resp(400, "ValidationFailed"));
            post.Parameter.Add(HttpMethod.PUT, _Base + "/kill-switches/{featureKey}", PutKillSwitchAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Change a kill switch", _Tag, "Body { killed, message?, rules?, blocksServer?, reason }. { killed, message?, rules?, blocksServer?, reason }: applies immediately (no content publish); killed without rules kills for everyone; rules target callers (for example one app version). Audited with the reason." + _Admin)
                    .PathParam("featureKey", "Engine feature key").Body("KillSwitchRequest").Returns("KillSwitch").Resp(400, "ValidationFailed or InvalidTargetingRule").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private AppConfigService App(HttpContextBase ctx)
        {
            return _Directory.For(ctx).LiveUpdates.AppConfig;
        }

        private async Task FlagsAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await App(ctx).EnumerateFlagsAsync(RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateFlagAsync(HttpContextBase ctx)
        {
            FeatureFlagRequest request = RouteHelper.ReadJson<FeatureFlagRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            FeatureFlagView view = await App(ctx).CreateFlagAsync(actor, request, present, Info(ctx), ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["ETag"] = view.ETag;
            await RouteHelper.SendJsonAsync(ctx, 201, view).ConfigureAwait(false);
        }

        private async Task GetFlagAsync(HttpContextBase ctx)
        {
            FeatureFlagDetailView view = await App(ctx).GetFlagAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["ETag"] = view.Flag.ETag;
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task UpdateFlagAsync(HttpContextBase ctx)
        {
            FeatureFlagRequest request = RouteHelper.ReadJson<FeatureFlagRequest>(ctx, out HashSet<string> present);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            FeatureFlagView view = await App(ctx).UpdateFlagAsync(actor, RouteHelper.Param(ctx, "id"), request, present, ctx.Request.RetrieveHeaderValue("If-Match"), Info(ctx), ctx.Token).ConfigureAwait(false);
            ctx.Response.Headers["ETag"] = view.ETag;
            await RouteHelper.SendJsonAsync(ctx, 200, view).ConfigureAwait(false);
        }

        private async Task ArchiveAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await App(ctx).ArchiveFlagAsync(RouteHelper.Param(ctx, "id"), true, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RestoreFlagAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await App(ctx).ArchiveFlagAsync(RouteHelper.Param(ctx, "id"), false, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteFlagAsync(HttpContextBase ctx)
        {
            await App(ctx).DeleteFlagAsync(RouteHelper.Param(ctx, "id"), Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task EvaluateAsync(HttpContextBase ctx)
        {
            FlagEvaluateRequest request = RouteHelper.ReadJson<FlagEvaluateRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await App(ctx).EvaluateFlagAsync(RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task HistoryAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await App(ctx).EnumerateHistoryAsync(RouteHelper.Param(ctx, "id"), RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DraftsAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await App(ctx).EnumerateDraftsAsync(RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task EnumerateDraftsAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await App(ctx).EnumerateDraftsAsync(RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DiscardAsync(HttpContextBase ctx)
        {
            DraftDiscardRequest request = RouteHelper.ReadJson<DraftDiscardRequest>(ctx);
            int discarded = await App(ctx).DiscardDraftsAsync(request, Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new Dictionary<string, int> { { "discarded", discarded } }).ConfigureAwait(false);
        }

        private async Task PublishAsync(HttpContextBase ctx)
        {
            ContentPublishRequest request = RouteHelper.ReadJson<ContentPublishRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 201, await App(ctx).PublishAsync(request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task VersionsAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await App(ctx).EnumerateVersionsAsync(RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task VersionAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await App(ctx).GetVersionAsync(RouteHelper.Param(ctx, "version"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ChangesAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await App(ctx).EnumerateChangesAsync(RouteHelper.Param(ctx, "version"), RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RestoreVersionAsync(HttpContextBase ctx)
        {
            ContentPublishRequest request = RouteHelper.ReadJson<ContentPublishRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 201, await App(ctx).RestoreVersionAsync(RouteHelper.Param(ctx, "version"), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task KillSwitchesAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await App(ctx).EnumerateKillSwitchesAsync(RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PutKillSwitchAsync(HttpContextBase ctx)
        {
            KillSwitchRequest request = RouteHelper.ReadJson<KillSwitchRequest>(ctx);
            KillSwitchView saved = await App(ctx).PutKillSwitchAsync(RouteHelper.Param(ctx, "featureKey"), request, Info(ctx), ctx.Token).ConfigureAwait(false);
            await MetricAnnotations.RecordAsync(_Directory.For(ctx), "KillSwitch", Guid.NewGuid().ToString("N"), request.Killed ? "Feature disabled" : "Feature restored", ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, saved).ConfigureAwait(false);
        }

        #endregion
    }
}
