namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// A property administrator's own domains and app ids (resolved decision 6, both modes): unique across the deployment,
    /// with DNS TXT ownership verification before a new domain is routed.
    /// </summary>
    public class PropertyDomainRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/property/domains";
        private const string _Tag = "Admin: property";
        private const string _Admin = " SystemAdmin or the admin token.";
        private const string _Shape = "{ propertyId, domains: [{ id, host, kind, verifiedUtc, source, verification: { recordName, recordValue, lastCheckedUtc, error } | null }], apps: [{ id, platform, appId, storeAppId, enabled }] }";
        private readonly PropertyDirectory _Directory;
        private readonly PlatformService _Platform;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory.</param>
        /// <param name="platform">Platform service (registry and conflict checks).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PropertyDomainRoutes(PropertyDirectory directory, PlatformService platform)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _Platform = platform ?? throw new ArgumentNullException(nameof(platform));
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
            EnumerationRoutes.Add(post, _Base, AdministrativeCatalogs.Domains, "PlatformDomainResponse",
                () => Doc("Enumerate property domains", _Tag, "Domains with propertyId context. Configuration entries without a creation timestamp use Unix epoch." + _Admin),
                async (ctx, query) =>
                {
                    ApplicationServices services = _Directory.For(ctx);
                    EnumerationRequest request = services.Enumeration.Prepare(query, AdministrativeCatalogs.Domains, true);
                    PropertyDomainsResponse result = await _Platform.PropertyDomainsAsync(services.PropertyId, ctx.Token).ConfigureAwait(false);
                    return CatalogEnumerationResponse<PlatformDomainResponse>.From(CatalogEnumeration.Page(request, result.Domains, item => item.Id, search: item => item.Host + " " + item.Kind), new Dictionary<string, object?> { ["propertyId"] = result.PropertyId });
                });
            EnumerationRoutes.Add(post, Constants.ApiPrefix + "/admin/property/apps", AdministrativeCatalogs.Apps, "PlatformAppResponse",
                () => Doc("Enumerate property applications", _Tag, "Registered applications with propertyId context. Configuration entries use Unix epoch." + _Admin),
                async (ctx, query) =>
                {
                    ApplicationServices services = _Directory.For(ctx);
                    EnumerationRequest request = services.Enumeration.Prepare(query, AdministrativeCatalogs.Apps, true);
                    PropertyDomainsResponse result = await _Platform.PropertyDomainsAsync(services.PropertyId, ctx.Token).ConfigureAwait(false);
                    return CatalogEnumerationResponse<PlatformAppResponse>.From(CatalogEnumeration.Page(request, result.Apps, item => item.Id, search: item => item.AppId + " " + item.Platform), new Dictionary<string, object?> { ["propertyId"] = result.PropertyId });
                });
            post.Static.Add(HttpMethod.PUT, _Base, PutAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Replace the property's domains and apps", _Tag, "Body { domains? [{ host, kind: Web | Api | Media | Redirect | Alias }], apps? [{ platform: Ios | Android, appId, storeAppId? }] }; a provided list replaces the stored one. Hosts and app ids are unique across the deployment. Unchanged hosts keep their state; new hosts start unverified and are not routed until POST .../{domainId}/verify succeeds. Audited property.domains.update." + _Admin)
                    .Body("PropertyDomainsUpdateRequest").Returns(_Shape).Bounded("PropertyDomains", "domains at most 20, apps at most 10").Resp(400, "ValidationFailed").Resp(409, "DomainInUse or AppIdInUse"));
            post.Parameter.Add(HttpMethod.POST, _Base + "/{domainId}/verify", VerifyAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Verify a domain", _Tag, "Looks up the TXT record verification.recordName (through deployment.domainVerificationResolverUrl) and marks the domain verified when it holds verification.recordValue; the check time and any error are stored either way. 200 with the domain. Audited property.domain.verify." + _Admin)
                    .PathParam("domainId", "Domain id").Returns("{ id, host, kind, verifiedUtc, source, verification }").Resp(404, "NotFound"));
        }

        #endregion

        #region Private-Methods

        private RequestInfo Info(HttpContextBase ctx)
        {
            return RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest);
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Platform.PropertyDomainsAsync(_Directory.For(ctx).PropertyId, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task PutAsync(HttpContextBase ctx)
        {
            PropertyDomainsUpdateRequest request = RouteHelper.ReadJson<PropertyDomainsUpdateRequest>(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await _Platform.UpdatePropertyDomainsAsync(_Directory.For(ctx), request, Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task VerifyAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Platform.VerifyDomainAsync(_Directory.For(ctx), RouteHelper.Param(ctx, "domainId"), Info(ctx), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
