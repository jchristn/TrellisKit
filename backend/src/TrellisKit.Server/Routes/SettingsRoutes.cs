namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;
    using TrellisKit.Server.Serialization;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin runtime settings routes backing the console's Settings view.
    /// </summary>
    public class SettingsRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/settings";
        private const string _Tag = "Settings";
        private readonly SettingsService _Settings;
        private readonly ServerLifetime _Lifetime;
        private readonly AppLogger _Logger;
        private readonly AuditService? _Audit;

        #endregion

        #region Public-Members

        /// <summary>
        /// Whether only the break-glass admin token may use these routes (MultiProperty: deployment settings belong to
        /// platform administrators at /api/v1/platform/settings; property admins get 403). Default false.
        /// </summary>
        public bool PlatformOnly { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate settings routes.
        /// </summary>
        /// <param name="settings">Settings service.</param>
        /// <param name="lifetime">Server lifetime, used for restarts.</param>
        /// <param name="logger">Logger.</param>
        /// <param name="audit">Audit service, or null to skip audit records.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public SettingsRoutes(SettingsService settings, ServerLifetime lifetime, AppLogger logger, AuditService? audit = null)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _Audit = audit;
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

            EnumerationRoutes.Add(server.Routes.PostAuthentication, _Base, AdministrativeCatalogs.Settings, "SettingField",
                () => OpenApiRouteMetadata.Create("Enumerate settings", _Tag).WithDescription("Editable settings; secret values omitted. Immutable field declarations use Unix epoch creation time. Admin only."),
                (ctx, query) =>
                {
                    if (PlatformOnly && (ctx.Metadata as RequestContext)?.PrincipalKind != PrincipalKindEnum.BreakGlass) throw new ApiErrorException(403, "Forbidden", "Deployment settings require a platform administrator.");
                    return Task.FromResult<object>(_Settings.Enumerate(query));
                });

            server.Routes.PostAuthentication.Static.Add(HttpMethod.PUT, _Base, PutAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Update settings", _Tag)
                    .WithDescription("Send only changed keys: { \"values\": { \"requestHistory.retentionDays\": 14 } }. Live fields apply immediately; restart fields apply after a restart. Admin only.")
                    .WithRequestBody(OpenApiRequestBodyMetadata.Json(OpenApiSchemaMetadata.Create("object"), "Changed values keyed by setting key."))
                    .WithResponse(200, OpenApiResponseMetadata.Create("Updated settings"))
                    .WithResponse(400, OpenApiResponseMetadata.Create("Validation failed; fieldErrors lists each problem")));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, _Base + "/restart", RestartAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Restart the server", _Tag)
                    .WithDescription("Exits the process so the container runtime restarts it and reloads the settings file. Admin only.")
                    .WithResponse(202, OpenApiResponseMetadata.Create("Restarting")));
        }

        #endregion

        #region Private-Methods

        private async Task GetAsync(HttpContextBase ctx)
        {
            if (await DeniedAsync(ctx).ConfigureAwait(false)) return;
            await RouteHelper.SendJsonAsync(ctx, 200, _Settings.Describe()).ConfigureAwait(false);
        }

        private async Task PutAsync(HttpContextBase ctx)
        {
            if (await DeniedAsync(ctx).ConfigureAwait(false)) return;
            SettingsUpdateRequest? request;
            try
            {
                request = ApiJson.Deserialize<SettingsUpdateRequest>(ctx.Request.DataAsString);
            }
            catch (JsonException)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "InvalidJson", "The request body is not valid JSON.").ConfigureAwait(false);
                return;
            }

            if (request == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "ValidationFailed", "A body of the form { \"values\": { ... } } is required.").ConfigureAwait(false);
                return;
            }

            try
            {
                SettingsResponse updated = await _Settings.UpdateAsync(request, ctx.Token).ConfigureAwait(false);
                if (_Audit != null && request.Values.Count > 0)
                    await _Audit.WriteAsync(RequestInfo.From(ctx, _Settings.Rest), "settings.update", "Settings", null, "Changed " + String.Join(", ", request.Values.Keys), ctx.Token).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, updated).ConfigureAwait(false);
            }
            catch (SettingsValidationException e)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "ValidationFailed", e.Message, e.FieldErrors).ConfigureAwait(false);
            }
        }

        private async Task RestartAsync(HttpContextBase ctx)
        {
            if (await DeniedAsync(ctx).ConfigureAwait(false)) return;
            _Logger.Warn("[SettingsRoutes] restart requested by admin from " + ClientAddressResolver.Resolve(ctx, _Settings.Rest));
            if (_Audit != null) await _Audit.WriteAsync(RequestInfo.From(ctx, _Settings.Rest), "server.restart", "Server", null, "Restart requested", ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 202, new MessageResponse("Restarting.")).ConfigureAwait(false);
            _Lifetime.RequestRestart();
        }

        private async Task<bool> DeniedAsync(HttpContextBase ctx)
        {
            if (!PlatformOnly) return false;
            RequestContext? context = ctx.Metadata as RequestContext;
            if (context != null && context.PrincipalKind == PrincipalKindEnum.BreakGlass) return false;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "In MultiProperty mode deployment settings are managed by platform administrators at /api/v1/platform/settings.").ConfigureAwait(false);
            return true;
        }

        #endregion
    }
}
