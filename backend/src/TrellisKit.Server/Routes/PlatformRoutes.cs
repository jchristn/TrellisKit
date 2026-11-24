namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.Routing;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Platform routes (/api/v1/platform/*): MultiProperty only (404 NotAvailable in SingleProperty), served only on a
    /// platform host (platform.hosts), authenticated with a platform administrator token (tkp_..., from
    /// POST /api/v1/platform/auth/login) or the break-glass admin token. Platform administrators are separate from
    /// every property's member accounts (docs/MULTIPROPERTY_CONTRACT.md section 6 with the resolved decisions).
    /// </summary>
    public class PlatformRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/platform";
        private const string _Tag = "Platform";
        private const string _PropertyItem = "PropertyRecord";
        private const string _PropertyBound = "domains (at most 20) and apps (at most 10)";
        private readonly PlatformService _Platform;
        private readonly SettingsService _Settings;
        private readonly ServerLifetime _Lifetime;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="platform">Platform service.</param>
        /// <param name="settings">Settings service (platform settings routes).</param>
        /// <param name="lifetime">Server lifetime (restart).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PlatformRoutes(PlatformService platform, SettingsService settings, ServerLifetime lifetime)
        {
            _Platform = platform ?? throw new ArgumentNullException(nameof(platform));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
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
            RoutingGroup g = server.Routes.PreAuthentication;
            const string auth = " Platform administrator token (Authorization: Bearer tkp_...) or the break-glass admin token; platform host only; MultiProperty only.";

            g.Static.Add(HttpMethod.POST, _Base + "/auth/login", LoginAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Platform sign-in", _Tag, "Body { email, password }. Returns a platform session token (platform.sessionMinutes). Anonymous; platform host only; MultiProperty only.")
                    .Body("PlatformLoginRequest").Returns("PlatformSession").Resp(400, "ValidationFailed").Resp(401, "InvalidCredentials").Resp(404, "NotAvailable or PropertyNotFound").Resp(423, "AccountLocked"));
            g.Static.Add(HttpMethod.POST, _Base + "/auth/accept-invite", AcceptInviteAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Accept a platform invitation", _Tag, "Body { token, password }. Sets the invited administrator's password. Anonymous; platform host only.")
                    .Body("PlatformAcceptInviteRequest").Resp(204, "Accepted").Resp(400, "InvalidToken or ValidationFailed"));
            g.Static.Add(HttpMethod.POST, _Base + "/auth/logout", LogoutAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Platform sign-out", _Tag, "Revokes the caller's platform session." + auth).Resp(204, "Signed out").Resp(401, "Unauthorized"));
            g.Static.Add(HttpMethod.GET, _Base + "/auth/me", MeAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Current platform administrator", _Tag, "The signed-in platform administrator." + auth)
                    .Bounded("PlatformAdmin", "labels and tags, at most 50 each").Resp(401, "Unauthorized").Resp(403, "Forbidden (break-glass token)"));

            EnumerationRoutes.Add(g, _Base + "/admins", PlatformCollections.PlatformAdmins, "PlatformAdmin",
                () => Doc("List platform administrators", _Tag, "Search over email and display name." + auth),
                async (ctx, query) => { await CallerAsync(ctx).ConfigureAwait(false); return await _Platform.EnumerateAdminsAsync(query, ctx.Token).ConfigureAwait(false); });
            g.Static.Add(HttpMethod.POST, _Base + "/admins", InviteAdminAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Invite a platform administrator", _Tag, "Body { email, displayName }. Returns the administrator and a one-time invitation token (platform.inviteDays)." + auth)
                    .Body("PlatformAdminInviteRequest").Returns("PlatformInvite", 201).Resp(400, "ValidationFailed").Resp(401, "Unauthorized").Resp(409, "EmailInUse"));
            g.Parameter.Add(HttpMethod.DELETE, _Base + "/admins/{adminId}", RemoveAdminAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Remove a platform administrator", _Tag, "Revokes their sessions; the row is kept for audit references." + auth)
                    .PathParam("adminId", "Platform administrator id (pad_)").Resp(204, "Removed").Resp(401, "Unauthorized").Resp(404, "NotFound").Resp(409, "LastPlatformAdmin or CannotRemoveOwnAdmin"));

            EnumerationRoutes.Add(g, _Base + "/properties", PlatformCollections.Properties, _PropertyItem,
                () => Doc("List properties", _Tag, "Registered properties (purged tombstones only with filters.status Deleted). Search over id, slug, and name; filters status, host." + auth),
                async (ctx, query) => { await CallerAsync(ctx).ConfigureAwait(false); return await _Platform.EnumeratePropertiesAsync(query, ctx.Token).ConfigureAwait(false); });
            g.Static.Add(HttpMethod.POST, _Base + "/properties", CreatePropertyAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Register a property", _Tag, "Body { name?, slug?, document? (a property.json object) or cloneFromPropertyId, domains?, apps?, labels?, tags? }. The id is always generated (app_{k-sortable}_{random}); new properties start Disabled. At most deployment.maxProperties (25)." + auth)
                    .Body("PropertyCreateRequest").Bounded(_PropertyItem, _PropertyBound).Resp(201, "Created").Resp(400, "ValidationFailed").Resp(401, "Unauthorized").Resp(404, "NotFound (clone source)")
                    .Resp(409, "PropertyLimitReached, DomainInUse, or AppIdInUse"));
            g.Parameter.Add(HttpMethod.GET, _Base + "/properties/{propertyId}", GetPropertyAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a property", _Tag, "The property record." + auth).PathParam("propertyId", "Property id")
                    .Bounded(_PropertyItem, _PropertyBound).Resp(401, "Unauthorized").Resp(404, "NotFound"));
            g.Parameter.Add(HttpMethod.PUT, _Base + "/properties/{propertyId}", UpdatePropertyAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update a property", _Tag, "Body { name?, domains? (replaces), apps? (replaces), labels?, tags? }. Hosts and app ids are unique across the deployment." + auth)
                    .PathParam("propertyId", "Property id").Body("PropertyUpdateRequest").Bounded(_PropertyItem, _PropertyBound).Resp(400, "ValidationFailed").Resp(401, "Unauthorized").Resp(404, "NotFound").Resp(409, "DomainInUse or AppIdInUse"));
            g.Parameter.Add(HttpMethod.POST, _Base + "/properties/{propertyId}/enable", EnableAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Enable a property", _Tag, "Readiness: a valid document and at least one Web domain." + auth)
                    .PathParam("propertyId", "Property id").Bounded(_PropertyItem, _PropertyBound).Resp(401, "Unauthorized").Resp(404, "NotFound").Resp(409, "PropertyNotReady"));
            g.Parameter.Add(HttpMethod.POST, _Base + "/properties/{propertyId}/disable", DisableAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Disable a property", _Tag, "Body { reason? }. Its requests return 404 PropertyNotFound within deployment.propertyCacheSeconds; sessions are kept." + auth)
                    .PathParam("propertyId", "Property id").Body("PropertyDisableRequest", false).Bounded(_PropertyItem, _PropertyBound).Resp(401, "Unauthorized").Resp(404, "NotFound").Resp(409, "PropertyNotReady (being deleted)"));
            g.Parameter.Add(HttpMethod.DELETE, _Base + "/properties/{propertyId}", DeletePropertyAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Delete a property", _Tag, "Body { confirm: <property id> }. Disabled immediately and restorable for platform.propertyPurgeDelayHours (720, 30 days); then every row and stored object of the property is purged. The id is never reused." + auth)
                    .PathParam("propertyId", "Property id").Body("PropertyDeleteRequest").Bounded(_PropertyItem, _PropertyBound).Resp(202, "Deleting").Resp(400, "ValidationFailed (confirmation)").Resp(401, "Unauthorized").Resp(404, "NotFound").Resp(409, "PropertyNotReady"));
            g.Parameter.Add(HttpMethod.POST, _Base + "/properties/{propertyId}/restore", RestoreAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Restore a deleted property", _Tag, "Back to Disabled, while the purge has not started." + auth)
                    .PathParam("propertyId", "Property id").Bounded(_PropertyItem, _PropertyBound).Resp(401, "Unauthorized").Resp(404, "NotFound").Resp(409, "PropertyNotRestorable"));

            EnumerationRoutes.Add(g, _Base + "/properties/{propertyId}/admins", PhaseOneCollections.AdminMembers, "UserSummary",
                () => Doc("List a property's administrators", _Tag, "Members of the property annotated as its SystemAdmin." + auth),
                async (ctx, query) => { await CallerAsync(ctx).ConfigureAwait(false); return await _Platform.EnumeratePropertyAdminsAsync(RouteHelper.Param(ctx, "propertyId"), query, ctx.Token).ConfigureAwait(false); },
                "propertyId");
            g.Parameter.Add(HttpMethod.POST, _Base + "/properties/{propertyId}/admins", GrantPropertyAdminAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Make a member a property administrator", _Tag, "Body { userId }: annotate a member of that property as its SystemAdmin. Audited in the platform audit and the property audit." + auth)
                    .PathParam("propertyId", "Property id").Body("PropertyAdminGrantRequest").Returns("UserSummary").Resp(401, "Unauthorized").Resp(404, "NotFound"));
            g.Parameter.Add(HttpMethod.DELETE, _Base + "/properties/{propertyId}/admins/{userId}", RevokePropertyAdminAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Remove a property administrator", _Tag, "Removes the member's SystemAdmin annotation." + auth)
                    .PathParam("propertyId", "Property id").PathParam("userId", "Member id").Returns("UserSummary").Resp(401, "Unauthorized").Resp(404, "NotFound"));
            g.Parameter.Add(HttpMethod.POST, _Base + "/properties/{propertyId}/admin-session", DelegatedSessionAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Act as a property's administrator", _Tag, "Returns a short-lived (platform.delegatedSessionMinutes), non-refreshable access token bound to that property; send it with X-TK-Property: <id>. The caller acts as SystemAdmin; routes that act on \"me\" return 403. Audited in the platform audit and the property audit. Platform administrators only." + auth)
                    .PathParam("propertyId", "Property id").Returns("DelegatedSession").Resp(401, "Unauthorized").Resp(403, "Forbidden (break-glass token)").Resp(404, "NotFound"));

            g.Parameter.Add(HttpMethod.POST, _Base + "/properties/{propertyId}/domains/{domainId}/verify", VerifyDomainAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Verify a property's domain", _Tag, "The same DNS TXT check as POST /api/v1/admin/property/domains/{domainId}/verify: marks the domain verified when _trelliskit-verify.<host> holds the challenge value; the check time and any error are stored either way. Audited property.domain.verify in the property audit and platform.domain.verify in the platform audit." + auth)
                    .PathParam("propertyId", "Property id").PathParam("domainId", "Domain id").Returns("{ id, host, kind, verifiedUtc, source, verification }").Resp(401, "Unauthorized").Resp(404, "NotFound"));
            g.Parameter.Add(HttpMethod.GET, _Base + "/request-history/{id}", ReadRequestHistoryAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a request history entry", _Tag, "One captured request of one property: query propertyId (a property id, platform (default), or unresolved). Headers and bodies are redacted at capture exactly as in the property request history (credentials, cookies, tokens, signatures, secret fields); bodies may be truncated." + auth)
                    .PathParam("id", "Request history entry id").QueryParam("propertyId", "Property id, platform (default), or unresolved").Returns("RequestHistoryEntry").Resp(401, "Unauthorized").Resp(404, "NotFound"));

            EnumerationRoutes.Add(g, _Base + "/audit", PlatformCollections.Audit, "AuditRecord",
                () => Doc("List platform audit records", _Tag, "The platform realm's audit by default; filters.propertyId reads one property's audit." + auth),
                async (ctx, query) => { await CallerAsync(ctx).ConfigureAwait(false); return await _Platform.EnumerateAuditAsync(query, ctx.Token).ConfigureAwait(false); });

            EnumerationRoutes.Add(g, _Base + "/request-history", PlatformCollections.RequestHistory, "RequestHistoryListItem",
                () => Doc("List request history across properties", _Tag, "One property's captured requests per call: filters.propertyId (a property id, platform for the platform realm (default), or unresolved), plus method, statusCode, pathContains, userId." + auth),
                async (ctx, query) => { await CallerAsync(ctx).ConfigureAwait(false); return await _Platform.EnumerateRequestHistoryAsync(query, ctx.Token).ConfigureAwait(false); });
            EnumerationRoutes.Add(g, _Base + "/metrics/properties", PlatformCollections.PropertyMetrics, "{ propertyId, slug, name, status, members, activeMembers, requests24h, errors24h, storageBytes, labels, tags }",
                () => Doc("Property metrics", _Tag, "One row per registered property (at most deployment.maxProperties): members, members seen in 30 days, requests and 5xx responses in 24 hours (request history), and stored media bytes. Search over id, slug, name; filter status. Orderings MembersDescending, ErrorsDescending, and StorageDescending sort by the computed metrics and page with skip only (no continuation token)." + auth),
                async (ctx, query) => { await CallerAsync(ctx).ConfigureAwait(false); return await _Platform.EnumeratePropertyMetricsAsync(query, ctx.Token).ConfigureAwait(false); });
            g.Static.Add(HttpMethod.POST, _Base + "/labels/enumerate", LabelsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Discover property labels", _Tag, "Labels on properties (targetType Property, the default) and platform administrators, most used first, for console autocomplete. Body MetadataDiscoveryQuery { targetType?, prefix?, maxResults, continuationToken }." + auth)
                    .Body("MetadataDiscoveryQuery", false).Enumeration("{ label, count }", "CreatedDescending, CreatedAscending", String.Empty, false).Resp(400, "ValidationFailed"));
            g.Static.Add(HttpMethod.POST, _Base + "/tags/keys/enumerate", TagKeysAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Discover property tag keys", _Tag, "Tag keys on properties (targetType Property, the default) and platform administrators." + auth)
                    .Body("MetadataDiscoveryQuery", false).Enumeration("{ key, count }", "CreatedDescending, CreatedAscending", String.Empty, false).Resp(400, "ValidationFailed"));
            g.Static.Add(HttpMethod.POST, _Base + "/tags/values/enumerate", TagValuesAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Discover property tag values", _Tag, "Values of one tag key (body key, required) on properties (targetType Property, the default)." + auth)
                    .Body("MetadataDiscoveryQuery { key }", false).Enumeration("{ value, count }", "CreatedDescending, CreatedAscending", String.Empty, false).Resp(400, "ValidationFailed"));
            EnumerationRoutes.Add(g, _Base + "/properties/{propertyId}/settings", AdministrativeCatalogs.PropertySettings, "PropertySettingField",
                () => Doc("Enumerate a property's settings", _Tag, "Standard property-scoped field enumeration. Immutable field declarations use Unix epoch." + auth),
                async (ctx, query) =>
                {
                    await CallerAsync(ctx).ConfigureAwait(false);
                    ApplicationServices services = RuntimeOf(ctx).Services;
                    EnumerationRequest request = services.Enumeration.Prepare(query, AdministrativeCatalogs.PropertySettings, true);
                    PropertySettingsResponse result = await services.PropertySettings.ReadAsync(ctx.Token).ConfigureAwait(false);
                    EnumerationResult<PropertySettingField> page = CatalogEnumeration.Page(request, result.Fields, field => field.Key, search: field => field.Key + " " + field.Section + " " + field.Label + " " + field.Description);
                    return CatalogEnumerationResponse<PropertySettingField>.From(page, new Dictionary<string, object?> { ["propertyId"] = result.PropertyId });
                }, "propertyId");
            g.Parameter.Add(HttpMethod.PUT, _Base + "/properties/{propertyId}/settings", PutPropertySettingsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update a property's integration settings", _Tag, "Body { values, clear }. Same body as PUT /api/v1/admin/property/settings. Audited property.settings.update in the platform and property audits." + auth)
                    .PathParam("propertyId", "Property id").Body("PropertySettingsUpdateRequest").Bounded("PropertySettings", "the property settings registry").Resp(400, "ValidationFailed").Resp(404, "NotFound"));

            EnumerationRoutes.Add(g, _Base + "/settings", AdministrativeCatalogs.Settings, "SettingField",
                () => Doc("Enumerate deployment settings", _Tag, "Secret values omitted. Immutable declarations use Unix epoch." + auth),
                async (ctx, query) => { await CallerAsync(ctx).ConfigureAwait(false); return _Settings.Enumerate(query); });
            g.Static.Add(HttpMethod.PUT, _Base + "/settings", PutSettingsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Update deployment settings", _Tag, "Same body as PUT /api/v1/settings: { \"values\": { ... } }. Audited platform.settings.update." + auth)
                    .Body("SettingsUpdateRequest").Resp(200, "Updated settings").Resp(400, "ValidationFailed").Resp(401, "Unauthorized"));
            g.Static.Add(HttpMethod.POST, _Base + "/settings/restart", RestartAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Restart the server", _Tag, "Exits the process so the container runtime restarts it. Audited platform.server.restart." + auth)
                    .Resp(202, "Restarting").Resp(401, "Unauthorized"));
        }

        #endregion

        #region Private-Methods

        private async Task<PlatformCaller> CallerAsync(HttpContextBase ctx)
        {
            PlatformCaller? caller = await _Platform.AuthenticateAsync(ctx.Request.RetrieveHeaderValue("Authorization"), ClientAddressResolver.Resolve(ctx, _Settings.Rest), ctx.Token).ConfigureAwait(false);
            if (caller == null) throw new ApiErrorException(401, "Unauthorized", "A valid platform administrator token is required.");
            return caller;
        }

        private MetadataDiscoveryQuery Discovery(HttpContextBase ctx)
        {
            MetadataDiscoveryQuery query = RouteHelper.ReadEnumerationBody<MetadataDiscoveryQuery>(ctx);
            if (String.IsNullOrWhiteSpace(query.TargetType)) query.TargetType = TargetTypeEnum.Property.ToString();
            return query;
        }

        private static ContentActor PlatformActor()
        {
            return new ContentActor { IsBreakGlass = true, IsSystemAdmin = true, IsStaff = true, HasMembership = true, AgeAttested = true };
        }

        private async Task LabelsAsync(HttpContextBase ctx)
        {
            await CallerAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Platform.RealmMetadata.EnumerateLabelsAsync(PlatformActor(), Discovery(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task TagKeysAsync(HttpContextBase ctx)
        {
            await CallerAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Platform.RealmMetadata.EnumerateTagKeysAsync(PlatformActor(), Discovery(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task TagValuesAsync(HttpContextBase ctx)
        {
            await CallerAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Platform.RealmMetadata.EnumerateTagValuesAsync(PlatformActor(), Discovery(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private PropertyRuntime RuntimeOf(HttpContextBase ctx)
        {
            PropertyRuntime? runtime = _Platform.Directory.Find(RouteHelper.Param(ctx, "propertyId"));
            if (runtime == null) throw ApiErrorException.NotFound("Property");
            return runtime;
        }

        private async Task GetPropertySettingsAsync(HttpContextBase ctx)
        {
            await CallerAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await RuntimeOf(ctx).Services.PropertySettings.ReadAsync(ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PutPropertySettingsAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            PropertyRuntime runtime = RuntimeOf(ctx);
            PropertySettingsUpdateRequest request = RouteHelper.ReadJson<PropertySettingsUpdateRequest>(ctx);
            RequestInfo info = new RequestInfo { SourceIp = caller.SourceIp, Context = new RequestContext { PropertyId = runtime.Id, UserId = caller.ActorId, PrincipalName = caller.ActorName } };
            PropertySettingsResponse response = await runtime.Services.PropertySettings.UpdateAsync(request, info, ctx.Token).ConfigureAwait(false);
            await _Platform.AuditAsync(caller, "property.settings.update", "Property", runtime.Id, "Changed " + String.Join(", ", request.Values.Keys.Concat(request.Clear).Distinct(StringComparer.Ordinal)), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task LoginAsync(HttpContextBase ctx)
        {
            PlatformLoginRequest request = RouteHelper.ReadJson<PlatformLoginRequest>(ctx);
            PlatformSessionResponse response = await _Platform.LoginAsync(request, ClientAddressResolver.Resolve(ctx, _Settings.Rest), ctx.Request.RetrieveHeaderValue("User-Agent"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task AcceptInviteAsync(HttpContextBase ctx)
        {
            await _Platform.AcceptInviteAsync(RouteHelper.ReadJson<PlatformAcceptInviteRequest>(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task LogoutAsync(HttpContextBase ctx)
        {
            await _Platform.LogoutAsync(await CallerAsync(ctx).ConfigureAwait(false), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task MeAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Platform.MeAsync(caller, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task InviteAdminAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            PlatformInviteResponse response = await _Platform.InviteAdminAsync(caller, RouteHelper.ReadJson<PlatformAdminInviteRequest>(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, response).ConfigureAwait(false);
        }

        private async Task RemoveAdminAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            await _Platform.RemoveAdminAsync(caller, RouteHelper.Param(ctx, "adminId"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendEmptyAsync(ctx, 204).ConfigureAwait(false);
        }

        private async Task CreatePropertyAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            PropertyRecordResponse response = await _Platform.CreatePropertyAsync(caller, RouteHelper.ReadJson<PropertyCreateRequest>(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, response).ConfigureAwait(false);
        }

        private async Task GetPropertyAsync(HttpContextBase ctx)
        {
            await CallerAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Platform.GetPropertyAsync(RouteHelper.Param(ctx, "propertyId"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task UpdatePropertyAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            PropertyRecordResponse response = await _Platform.UpdatePropertyAsync(caller, RouteHelper.Param(ctx, "propertyId"), RouteHelper.ReadJson<PropertyUpdateRequest>(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task EnableAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Platform.EnableAsync(caller, RouteHelper.Param(ctx, "propertyId"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DisableAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            PropertyDisableRequest request = String.IsNullOrWhiteSpace(ctx.Request.DataAsString) ? new PropertyDisableRequest() : RouteHelper.ReadJson<PropertyDisableRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Platform.DisableAsync(caller, RouteHelper.Param(ctx, "propertyId"), request.Reason, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DeletePropertyAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            PropertyDeleteRequest request = String.IsNullOrWhiteSpace(ctx.Request.DataAsString) ? new PropertyDeleteRequest() : RouteHelper.ReadJson<PropertyDeleteRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 202, await _Platform.DeleteAsync(caller, RouteHelper.Param(ctx, "propertyId"), request.Confirm, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task VerifyDomainAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            PropertyRuntime runtime = RuntimeOf(ctx);
            RequestInfo info = new RequestInfo { SourceIp = caller.SourceIp, Context = new RequestContext { PropertyId = runtime.Id, UserId = caller.ActorId, PrincipalName = caller.ActorName } };
            PlatformDomainResponse domain = await _Platform.VerifyDomainAsync(runtime.Services, RouteHelper.Param(ctx, "domainId"), info, ctx.Token).ConfigureAwait(false);
            await _Platform.AuditAsync(caller, "platform.domain.verify", "Property", runtime.Id, domain.Host + (domain.VerifiedUtc != null ? ": verified" : ": not verified"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, domain).ConfigureAwait(false);
        }

        private async Task ReadRequestHistoryAsync(HttpContextBase ctx)
        {
            await CallerAsync(ctx).ConfigureAwait(false);
            RequestHistoryEntry entry = await _Platform.ReadRequestHistoryAsync(RouteHelper.Query(ctx, "propertyId"), RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, entry).ConfigureAwait(false);
        }

        private async Task RestoreAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Platform.RestoreAsync(caller, RouteHelper.Param(ctx, "propertyId"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task GrantPropertyAdminAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            PropertyAdminGrantRequest request = RouteHelper.ReadJson<PropertyAdminGrantRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Platform.SetPropertyAdminAsync(caller, RouteHelper.Param(ctx, "propertyId"), request.UserId, true, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RevokePropertyAdminAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Platform.SetPropertyAdminAsync(caller, RouteHelper.Param(ctx, "propertyId"), RouteHelper.Param(ctx, "userId"), false, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task DelegatedSessionAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            DelegatedSessionResponse response = await _Platform.CreateDelegatedSessionAsync(caller, RouteHelper.Param(ctx, "propertyId"), ctx.Request.RetrieveHeaderValue("User-Agent"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task GetSettingsAsync(HttpContextBase ctx)
        {
            await CallerAsync(ctx).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, _Settings.Describe()).ConfigureAwait(false);
        }

        private async Task PutSettingsAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            SettingsUpdateRequest request = RouteHelper.ReadJson<SettingsUpdateRequest>(ctx);
            SettingsResponse updated;
            try
            {
                updated = await _Settings.UpdateAsync(request, ctx.Token).ConfigureAwait(false);
            }
            catch (SettingsValidationException e)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "ValidationFailed", e.Message, e.FieldErrors).ConfigureAwait(false);
                return;
            }
            if (request.Values.Count > 0) await _Platform.AuditAsync(caller, "platform.settings.update", "Settings", null, "Changed " + String.Join(", ", request.Values.Keys), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, updated).ConfigureAwait(false);
        }

        private async Task RestartAsync(HttpContextBase ctx)
        {
            PlatformCaller caller = await CallerAsync(ctx).ConfigureAwait(false);
            await _Platform.AuditAsync(caller, "platform.server.restart", "Server", null, "Restart requested", ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 202, new MessageResponse("Restarting.")).ConfigureAwait(false);
            _Lifetime.RequestRestart();
        }

        #endregion
    }
}
