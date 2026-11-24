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
    /// The property's access model configuration in the admin portal (docs/ACCESS_MODELS_CONTRACT.md section 6): read
    /// and change the access model, anonymous browsing, gated features, and default audience. SystemAdmin, the admin
    /// token, or a delegated platform session, in both deployment modes.
    /// </summary>
    public class PropertyAccessRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/admin/property/access";
        private const string _Tag = "Admin: property";
        private const string _Admin = " SystemAdmin or the admin token.";
        private const string _Shape = "AccessPolicy { model, anonymousBrowsing, gatedFeatures, defaultAudience, purchasesEnabled, signupCodesEnabled, revision, configuredDefaultAudience, updatedByUserId, updatedUtc }";
        private const string _Bound = "gatedFeatures at most the 8 feature keys";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public PropertyAccessRoutes(PropertyDirectory directory)
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
            post.Static.Add(HttpMethod.GET, _Base, GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read the access model", _Tag, "The stored access configuration; carries { model (Open, PartiallyGated, FullyGated), anonymousBrowsing, gatedFeatures (as stored, also outside PartiallyGated; feature keys feed, members, forums, library, events, directory, messaging, assistants, search), defaultAudience (effective), configuredDefaultAudience (null means the model default), purchasesEnabled, signupCodesEnabled, revision, updatedByUserId, updatedUtc }." + _Admin)
                    .Returns(_Shape).Bounded("AccessPolicy", _Bound));
            post.Static.Add(HttpMethod.PUT, _Base, PutAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Change the access model", _Tag, "Body { model?, anonymousBrowsing?, gatedFeatures?: [feed | members | forums | library | events | directory | messaging | assistants | search], defaultAudience?: Public | SignedIn | Members | null }; omitted fields keep their value. " +
                    "defaultAudience Members is rejected in Open; a paid model needs a membership section in the property document. Applies to every request immediately; other instances within deployment.propertyCacheSeconds. Subscriptions and entitlements are never changed, so switching back restores members. Audited property.access.update." + _Admin)
                    .Body("AccessPolicyRequest").Returns(_Shape).Bounded("AccessPolicy", _Bound).Resp(400, "ValidationFailed"));
        }

        #endregion

        #region Private-Methods

        private async Task GetAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, _Directory.For(ctx).Access.View()).ConfigureAwait(false);
        }

        private async Task PutAsync(HttpContextBase ctx)
        {
            AccessPolicyRequest request = RouteHelper.ReadJson<AccessPolicyRequest>(ctx, out HashSet<string> present);
            ApplicationServices services = _Directory.For(ctx);
            await RouteHelper.SendJsonAsync(ctx, 200, await services.Access.UpdateAsync(RequestInfo.From(ctx, services.Settings.Rest), request, present, ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
