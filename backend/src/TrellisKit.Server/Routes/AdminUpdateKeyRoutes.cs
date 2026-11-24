namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin live update registry routes (docs/LIVE_UPDATES_CONTRACT.md sections 6.3, 7.4, 7.5, 10.1): channels,
    /// runtime versions, deploy keys, signing keys and rotation, installs by key id, and health. SystemAdmin or the
    /// admin token; signing key import and activation need a member session.
    /// </summary>
    public class AdminUpdateKeyRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/updates";
        private const string _Tag = "Admin: live updates";
        private const string _Admin = " SystemAdmin or the admin token.";
        private const string _Release = " SystemAdmin, ReleaseManager, or the admin token.";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminUpdateKeyRoutes(PropertyDirectory directory)
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
            post.Static.Add(HttpMethod.GET, _Base + "/health", HealthAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Live updates health", _Tag, "Signing (Active or Missing) with the problem and storage. Latest releases are in the channel enumeration. Deploy keys may call it." + _Release)
                    .Returns("UpdateHealth"));
            EnumerationRoutes.Add(post, _Base + "/channels", LiveUpdateCollections.Channels, "UpdateChannel",
                () => Doc("Enumerate update channels", _Tag, "Built-in and custom channels with latestByPlatform and activeInstalls30d. Filter builtIn." + _Release),
                async (ctx, query) => await Live(ctx).Admin.EnumerateChannelsAsync(query, ctx.Token).ConfigureAwait(false));
            post.Static.Add(HttpMethod.POST, _Base + "/channels", CreateChannelAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Add a custom channel", _Tag, "Body { name }. { name } matching ^[a-z][a-z0-9-]{1,31}$, at most updates.maxChannels custom channels." + _Release)
                    .Body("UpdateChannelRequest").Returns("UpdateChannel", 201).Resp(400, "ValidationFailed").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.DELETE, _Base + "/channels/{name}", DeleteChannelAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a custom channel", _Tag, "Only custom channels without Published or Paused groups." + _Release).PathParam("name", "Channel").Resp(204, "Deleted").Resp(404, "NotFound").Resp(409, "Conflict"));
            post.Static.Add(HttpMethod.POST, _Base + "/runtime-versions/enumerate", RuntimeVersionsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate runtime versions", _Tag, "Per platform and runtime version from install rows: first and last seen, activeInstalls30d, latestUpdate, binaryVersions (at most 10). Filters platform, channel; orderings LastSeenDescending (default), CreatedDescending, CreatedAscending (first seen)." + _Release)
                    .Body("EnumerationQuery", false).Enumeration("UpdateRuntimeVersion", "LastSeenDescending, CreatedDescending, CreatedAscending", "platform, channel", false).Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, _Base + "/deploy-keys/enumerate", DeployKeysAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate deploy keys", _Tag, "Filter active; search over name. Secrets are never returned." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration("UpdateDeployKey", "CreatedDescending, CreatedAscending", "active", false).Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, _Base + "/deploy-keys", CreateDeployKeyAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Create a deploy key", _Tag, "Body { name, channels, expiresUtc?, labels?, tags? }. { name, channels (never production), expiresUtc?, labels?, tags? }. The tkdk_ secret is returned once. A deploy key may only upload, check assets, and publish on its channels." + _Admin)
                    .Body("UpdateDeployKeyRequest").Returns("UpdateDeployKey", 201).Resp(400, "ValidationFailed").Resp(403, "Forbidden"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/deploy-keys/{id}/revoke", RevokeDeployKeyAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Revoke a deploy key", _Tag, _Admin.Trim()).PathParam("id", "Key id").Body("Body", false).Returns("UpdateDeployKey").Resp(404, "NotFound"));
            post.Static.Add(HttpMethod.POST, _Base + "/signing-keys/enumerate", SigningKeysAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enumerate signing keys", _Tag, "Status, certificate fingerprint and expiry, issuing key, whether the private key is available, last use. No private material. Filter status; orderings CreatedDescending, CreatedAscending, KeyIdAscending." + _Admin)
                    .Body("EnumerationQuery", false).Enumeration("UpdateSigningKey", "CreatedDescending, CreatedAscending, KeyIdAscending", "status", false).Resp(400, "ValidationFailed"));
            post.Static.Add(HttpMethod.POST, _Base + "/signing-keys", CreateSigningKeyAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Register a signing key", _Tag, "Body { keyId, certificatePem, privateKeyPem?, status? }. { keyId, certificatePem, privateKeyPem?, status? }. With privateKeyPem (member session only) the key is stored encrypted with secrets.encryptionKey and never returned; without it the deployment supplies the key (TK_UPDATES_SIGNING_KEY or TK_UPDATES_STANDBY_SIGNING_KEY). The key must match the certificate.")
                    .Body("UpdateSigningKeyRequest").Returns("UpdateSigningKey", 201).Resp(400, "ValidationFailed").Resp(403, "Forbidden").Resp(409, "Conflict").Resp(503, "IntegrationNotConfigured"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/signing-keys/{id}/activate", ActivateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Switch signing to a key", _Tag, "Rotation: the key signs every manifest from now on; binaries that embed another certificate receive the certificate chain. Member session only.")
                    .PathParam("id", "Key record id").Body("Body", false).Returns("UpdateSigningKey").Resp(403, "Forbidden").Resp(404, "NotFound").Resp(409, "Conflict"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/signing-keys/{id}/retire", RetireAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Retire a signing key", _Tag, "Never signs again; manifests requested with its key id get 503 SigningUnavailable." + _Admin)
                    .PathParam("id", "Key record id").Body("Body", false).Returns("UpdateSigningKey").Resp(404, "NotFound"));
            EnumerationRoutes.Add(post, _Base + "/signing-keys/installs", LiveUpdateCollections.SigningKeyInstalls, "UpdateCountRow",
                () => Doc("Enumerate installs by embedded key id", _Tag, "Active installs (30 days) grouped by their binary's embedded key. Dates use earliest first-seen time; metadata is empty." + _Admin),
                async (ctx, query) => await Live(ctx).Admin.InstallsByKeyIdAsync(query, ctx.Token).ConfigureAwait(false));
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private LiveUpdateServices Live(HttpContextBase ctx)
        {
            return _Directory.For(ctx).LiveUpdates;
        }

        private static bool MemberSession(HttpContextBase ctx)
        {
            RequestContext? context = ctx.Metadata as RequestContext;
            return context != null && !String.IsNullOrEmpty(context.UserId);
        }

        private async Task HealthAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Admin.HealthAsync(ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateChannelAsync(HttpContextBase ctx)
        {
            UpdateChannelRequest request = RouteHelper.ReadJson<UpdateChannelRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 201, await Live(ctx).Admin.CreateChannelAsync(request, Info(ctx), _Directory.For(ctx).Audit, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeleteChannelAsync(HttpContextBase ctx)
        {
            await Live(ctx).Admin.DeleteChannelAsync(RouteHelper.Param(ctx, "name"), Info(ctx), _Directory.For(ctx).Audit, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task RuntimeVersionsAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Admin.EnumerateRuntimeVersionsAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeployKeysAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Admin.EnumerateDeployKeysAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateDeployKeyAsync(HttpContextBase ctx)
        {
            UpdateDeployKeyRequest request = RouteHelper.ReadJson<UpdateDeployKeyRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await Live(ctx).Admin.CreateDeployKeyAsync(actor, request, Info(ctx), _Directory.For(ctx).Audit, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RevokeDeployKeyAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Admin.RevokeDeployKeyAsync(RouteHelper.Param(ctx, "id"), Info(ctx), _Directory.For(ctx).Audit, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SigningKeysAsync(HttpContextBase ctx)
        {
            EnumerationQuery query = RouteHelper.ReadEnumerationBody<EnumerationQuery>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Admin.EnumerateSigningKeysAsync(query, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateSigningKeyAsync(HttpContextBase ctx)
        {
            UpdateSigningKeyRequest request = RouteHelper.ReadJson<UpdateSigningKeyRequest>(ctx);
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            await _Directory.For(ctx).Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, ctx.Token, TrellisKit.Core.Enums.TargetTypeEnum.UpdateSigningKey).ConfigureAwait(false);
            UpdateSigningKey key = await Live(ctx).Signing.RegisterAsync(request, MemberSession(ctx), ctx.Token).ConfigureAwait(false);
            await _Directory.For(ctx).Metadata.ApplyAsync(actor, TrellisKit.Core.Enums.TargetTypeEnum.UpdateSigningKey, key.Id, request.Labels, request.Tags, ctx.Token).ConfigureAwait(false);
            await _Directory.For(ctx).Audit.WriteAsync(Info(ctx), "updates.signingKey.create", "UpdateSigningKey", key.Id, key.KeyId + " (" + key.Source + ", " + key.Status + ")", ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, await Live(ctx).Admin.SigningKeyViewAsync(key.Id, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ActivateAsync(HttpContextBase ctx)
        {
            if (!MemberSession(ctx)) throw TrellisKit.Core.Exceptions.ApiErrorException.Forbidden("Switching the signing key needs a member session.");
            UpdateSigningKey key = await Live(ctx).Signing.ActivateAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await _Directory.For(ctx).Audit.WriteAsync(Info(ctx), "updates.signingKey.activate", "UpdateSigningKey", key.Id, key.KeyId, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Admin.SigningKeyViewAsync(key.Id, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RetireAsync(HttpContextBase ctx)
        {
            UpdateSigningKey key = await Live(ctx).Signing.RetireAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await _Directory.For(ctx).Audit.WriteAsync(Info(ctx), "updates.signingKey.retire", "UpdateSigningKey", key.Id, key.KeyId, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await Live(ctx).Admin.SigningKeyViewAsync(key.Id, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }


        #endregion
    }
}
