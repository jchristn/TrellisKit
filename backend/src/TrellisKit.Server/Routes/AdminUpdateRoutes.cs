namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin live update routes (docs/LIVE_UPDATES_CONTRACT.md sections 7 and 10): the publish tool's upload, asset
    /// existence checks, update groups, and their lifecycle (publish, rollout and rollout plans, pause, resume,
    /// critical, rollback, rollback to embedded, promotion, delete). SystemAdmin, the admin token, or a deploy key
    /// (upload, asset HEAD, and publish only, on its channels).
    /// </summary>
    public class AdminUpdateRoutes
    {
        #region Private-Members

        private const string _Updates = Constants.ApiPrefix + "/admin/updates";
        private const string _Groups = Constants.ApiPrefix + "/admin/update-groups";
        private const string _Tag = "Admin: live updates";
        private const string _Admin = " SystemAdmin, ReleaseManager, or the admin token.";
        private const string _Release = " SystemAdmin, ReleaseManager, or the admin token.";
        private const string _Orderings = "CreatedDescending, CreatedAscending, UpdatedDescending";
        private const string _Filters = "platform, channel, runtimeVersion, status, kind, critical, gitCommit";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminUpdateRoutes(PropertyDirectory directory)
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
            post.Static.Add(HttpMethod.POST, _Updates, UploadAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Upload and publish an update", _Tag, "multipart/form-data: an update part (the descriptor: platform, runtimeVersion, channel, message, critical, criticalMessage, rolloutPercentage, gitCommit, createdUtc, updateGroupId, expoConfig, launchAsset, assets, sourceMaps, supersede, labels, tags), then one part per asset named by its key (MD5 hex) and one sourcemap-<md5> part per source map. " +
                    "Every part is verified (SHA-256 and MD5) and stored content addressed; assets already stored may be omitted. Source maps are stored privately and never served. Platforms of one run share updateGroupId. rolloutPercentage 0 keeps a Draft. SystemAdmin, the admin token, or a deploy key for the channel.")
                    .Body("multipart/form-data upload").Returns("UpdateGroup", 201).Resp(400, "ValidationFailed, UnknownChannel, UnsupportedPlatform, or AssetMismatch").Resp(403, "Forbidden")
                    .Resp(409, "AssetsMissing, RolloutConflict, or Conflict").Resp(413, "TooLarge").Resp(503, "SigningUnavailable (kept as Draft)"));
            post.Parameter.Add(HttpMethod.HEAD, _Updates + "/assets/{sha256Hex}", HeadAssetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Check a stored asset", _Tag, "200 when the property stores the asset, 404 when missing; no body. Deploy keys may call it." + _Admin)
                    .PathParam("sha256Hex", "SHA-256 hex").Resp(200, "Stored").Resp(404, "Missing"));
            post.Parameter.Add(HttpMethod.POST, _Updates + "/{id}/assets/enumerate", AssetsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate an update's assets", _Tag, "EnumerationQuery; ordering PositionAscending (default), CreatedDescending, CreatedAscending; search over key and content type." + _Admin)
                    .PathParam("id", "Update id (upd_)").Body("EnumerationQuery", false).Enumeration("{ hash, key, contentType, fileExtension, sizeBytes, isLaunchAsset }", "PositionAscending, CreatedDescending, CreatedAscending", String.Empty, false).Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Updates + "/{id}/failures/enumerate", FailuresAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate an update's reported failures", _Tag, "Client-reported failures (LaunchFailed, DownloadFailed, CheckFailed) that carried a message or stack trace: id, type, errorCode, message, hasStackTrace, occurredUtc, receivedUtc. Filter type; newest first. Stack traces are private." + _Release)
                    .PathParam("id", "Update id (upd_)").Body("EnumerationQuery", false).Enumeration("UpdateFailure", "CreatedDescending, CreatedAscending", "type", false).Resp(400, "ValidationFailed").Resp(404, "NotFound"));
            post.Parameter.Add(HttpMethod.POST, _Updates + "/{id}/symbolicate", SymbolicateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Symbolicate a stack trace", _Tag, "Body { stackTrace } (at most 65536 characters) or { eventId } (a reported failure of this update). Maps each frame through the update's private source map on the server: { updateId, sourceMapAvailable, sourceMap, frames (at most 200, each with raw, function, file, line, column, original), text, truncated }. Source maps are never served." + _Release)
                    .PathParam("id", "Update id (upd_)").Body("SymbolicateRequest").Returns("Symbolication").Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(413, "TooLarge").Resp(422, "SourceMapInvalid"));
            post.Static.Add(HttpMethod.GET, _Groups, ListAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("List update groups", _Tag, "Search covers message and git commit." + _Admin).Enumeration("UpdateGroup", _Orderings, _Filters).Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, _Groups + "/enumerate", EnumerateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate update groups", _Tag, "EnumerationQuery; filters platform, channel, runtimeVersion, status, kind, critical, gitCommit." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration("UpdateGroup", _Orderings, _Filters, false).Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, _Groups + "/rollback-to-embedded", RollbackToEmbeddedAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Roll back to the built-in bundle", _Tag, "Body { platform: ios | android | all, runtimeVersion, channel, rolloutPercentage?, message? }. Installs on the target get a signed rollBackToEmbedded directive; any later publish supersedes it. Not for deploy keys." + _Admin)
                    .Body("UpdateRollbackRequest").Returns("UpdateGroup", 201).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(503, "SigningUnavailable"));
            post.Parameter.Add(HttpMethod.GET, _Groups + "/{id}", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read an update group", _Tag, "UpdateGroup with updates (fixed bound 2), rolloutPlan (at most 10), autoHalt counts, adoption, and assets { objects (max 20), totalRecords, enumerate }." + _Admin)
                    .PathParam("id", "Group id").Bounded("UpdateGroup", "updates at most 2 (one per platform), rolloutPlan at most 10 stages, assets preview at most 20 with totalRecords and the enumerate path").Resp(404, "NotFound"));
            EnumerationRoutes.Add(post, _Groups + "/{id}/adoption", LiveUpdateCollections.AdoptionDays, "UpdateAdoptionDay",
                () => Doc("Enumerate update adoption days", _Tag, "Filter days (1 to 90, default 14). Scalar days and byPlatform context accompany the standard page." + _Admin),
                async (ctx, query) => await Live(ctx).Admin.AdoptionAsync(RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false), "id");
            EnumerationRoutes.Add(post, _Groups + "/{id}/adoption/binary-versions", LiveUpdateCollections.AdoptionBinaries, "UpdateCountRow",
                () => Doc("Enumerate adoption by binary version", _Tag, "Exact install counts across the group's platform updates, active within 30 days. No top-N truncation." + _Admin),
                async (ctx, query) => await Live(ctx).Admin.EnumerateAdoptionBinariesAsync(RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false), "id");
            Lifecycle(post, "publish", "Publish a draft group", "{ rolloutPercentage?, supersede? }. Deploy keys may publish groups on their channels.", PublishAsync, "409 UpdateNotDraft or RolloutConflict; 503 SigningUnavailable");
            Lifecycle(post, "rollout", "Change the rollout percentage", "{ rolloutPercentage, platform? }: a manual override at any time; stages of a rollout plan still apply at their time.", RolloutAsync, "409 UpdateNotPublished");
            Lifecycle(post, "pause", "Pause a rollout", "{ reason? }: installs not running it stop receiving it.", PauseAsync, "409 UpdateNotPublished");
            Lifecycle(post, "resume", "Resume a rollout", "{ reason? }: also clears an automatic halt.", ResumeAsync, "409 UpdateNotPublished");
            Lifecycle(post, "critical", "Mark an update critical", "{ critical, criticalMessage? (200 characters) }: applies to installs that have not downloaded it.", CriticalAsync, "400 ValidationFailed");
            Lifecycle(post, "rollback", "Roll back to an earlier group", "{ targetUpdateGroupId, rolloutPercentage?, message? }: republishes the target's content with new ids and createdAt; this group becomes RolledBack.", RollbackAsync, "400 ValidationFailed; 503 SigningUnavailable");
            Lifecycle(post, "promote", "Promote to another channel", "{ channel, rolloutPercentage?, message?, supersede? }: republishes the group on the channel (never from or to development); promotedFrom records the source.", PromoteAsync, "400 PromotionNotAllowed or UnknownChannel; 409 RolloutConflict; 503 SigningUnavailable");
            post.Parameter.Add(HttpMethod.PUT, _Groups + "/{id}/rollout-plan", PlanAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Set the rollout plan", _Tag, "Body { stages, useTemplate, autoHaltEnabled, autoHaltFailurePercent, autoHaltMinInstalls }. { stages?: [{ percentage, startsUtc? | hoursAfterPublish? }] (at most 10, increasing), useTemplate?, autoHaltEnabled?, autoHaltFailurePercent?, autoHaltMinInstalls? }. The worker applies each stage at its time; the automatic halt pauses the group when the launch failure rate exceeds the threshold." + _Admin)
                    .PathParam("id", "Group id").Body("UpdateRolloutPlanRequest").Returns("UpdateGroup").Resp(400, "ValidationFailed").Resp(404, "NotFound").Resp(409, "UpdateNotPublished"));
            post.Parameter.Add(HttpMethod.DELETE, _Groups + "/{id}", DeleteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a draft group", _Tag, "Drafts only; published history is permanent." + _Admin).PathParam("id", "Group id").Resp(204, "Deleted").Resp(404, "NotFound").Resp(409, "UpdateNotDraft"));
        }

        #endregion

        #region Private-Methods

        private void Lifecycle(WatsonWebserver.Core.Routing.RoutingGroup post, string action, string summary, string description, Func<HttpContextBase, Task> handler, string errors)
        {
            post.Parameter.Add(HttpMethod.POST, _Groups + "/{id}/" + action, handler, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc(summary, _Tag, description + _Admin).PathParam("id", "Group id").Body("Body", false).Returns("UpdateGroup", action == "rollback" || action == "promote" ? 201 : 200).Resp(404, "NotFound").Resp(400, errors));
        }

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private LiveUpdateServices Live(HttpContextBase ctx)
        {
            return _Directory.For(ctx).LiveUpdates;
        }

        private async Task UploadAsync(HttpContextBase ctx)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            Stream body = ctx.Request.ChunkedTransfer ? new ChunkedRequestStream(ctx.Request) : ctx.Request.Data;
            await RouteHelper.SendJsonAsync(ctx, 201, await Live(ctx).Publish.UploadAsync(actor, body, ctx.Request.ContentType, ctx.Request.ContentLength, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task HeadAssetAsync(HttpContextBase ctx)
        {
            bool stored = await Live(ctx).Publish.HasAssetAsync(RouteHelper.Param(ctx, "sha256Hex"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, stored ? 200 : 404).ConfigureAwait(false);
        }

        private async Task AssetsAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Admin.EnumerateAssetsAsync(RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationQuery<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Admin.EnumerateGroupsAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task EnumerateAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Admin.EnumerateGroupsAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Admin.GetGroupAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PublishAsync(HttpContextBase ctx)
        {
            UpdateRolloutRequest request = RouteHelper.ReadJson<UpdateRolloutRequest>(ctx);
            TrellisKit.Core.Responses.UpdateGroupView published = await Live(ctx).Publish.PublishAsync(RouteHelper.Param(ctx, "id"), request, Info(ctx), ctx.Token).ConfigureAwait(false);
            await MetricAnnotations.RecordAsync(_Directory.For(ctx), "LiveUpdate", "update:" + RouteHelper.Param(ctx, "id"), "Live update published", ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, published).ConfigureAwait(false);
        }

        private async Task RolloutAsync(HttpContextBase ctx)
        {
            UpdateRolloutRequest request = RouteHelper.ReadJson<UpdateRolloutRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Publish.RolloutAsync(RouteHelper.Param(ctx, "id"), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PauseAsync(HttpContextBase ctx)
        {
            UpdateRolloutRequest request = RouteHelper.ReadJson<UpdateRolloutRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Publish.PauseAsync(RouteHelper.Param(ctx, "id"), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ResumeAsync(HttpContextBase ctx)
        {
            UpdateRolloutRequest request = RouteHelper.ReadJson<UpdateRolloutRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Publish.ResumeAsync(RouteHelper.Param(ctx, "id"), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CriticalAsync(HttpContextBase ctx)
        {
            UpdateCriticalRequest request = RouteHelper.ReadJson<UpdateCriticalRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Publish.CriticalAsync(RouteHelper.Param(ctx, "id"), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RollbackAsync(HttpContextBase ctx)
        {
            UpdateRollbackRequest request = RouteHelper.ReadJson<UpdateRollbackRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 201, await Live(ctx).Publish.RollbackAsync(RouteHelper.Param(ctx, "id"), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PromoteAsync(HttpContextBase ctx)
        {
            UpdateRolloutRequest request = RouteHelper.ReadJson<UpdateRolloutRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 201, await Live(ctx).Publish.PromoteAsync(RouteHelper.Param(ctx, "id"), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RollbackToEmbeddedAsync(HttpContextBase ctx)
        {
            UpdateRollbackRequest request = RouteHelper.ReadJson<UpdateRollbackRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 201, await Live(ctx).Publish.RollbackToEmbeddedAsync(request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PlanAsync(HttpContextBase ctx)
        {
            UpdateRolloutPlanRequest request = RouteHelper.ReadJson<UpdateRolloutPlanRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Publish.SetRolloutPlanAsync(RouteHelper.Param(ctx, "id"), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task FailuresAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Symbolication.EnumerateFailuresAsync(RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SymbolicateAsync(HttpContextBase ctx)
        {
            SymbolicateRequest request = RouteHelper.ReadJson<SymbolicateRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Symbolication.SymbolicateAsync(RouteHelper.Param(ctx, "id"), request, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            await Live(ctx).Publish.DeleteAsync(RouteHelper.Param(ctx, "id"), Info(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        #endregion
    }
}
