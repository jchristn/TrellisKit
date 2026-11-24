namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Requests;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Admin subscription routes and membership metrics. SystemAdmin or the break-glass token.
    /// </summary>
    public class AdminSubscriptionRoutes
    {
        #region Private-Members

        private const string _Tag = "Admin: subscriptions";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public AdminSubscriptionRoutes(PropertyDirectory directory)
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
            string baseUrl = Constants.ApiPrefix + "/admin/subscriptions";
            EnumerationRoutes.Add(server.Routes.PostAuthentication, baseUrl, PhaseOneCollections.Subscriptions, "AdminSubscriptionItem",
                () => Doc("List subscriptions", _Tag, "Search matches member name, email, and store subscription id. Filters: store (Apple, Google, Stripe), status (Active, InGrace, InBillingRetry, Expired, Revoked, Refunded, Paused), userId. SystemAdmin or the admin token."),
                async (ctx, query) => await _Directory.For(ctx).AdminSubscriptions.ListAsync(query, ctx.Token).ConfigureAwait(false));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, baseUrl + "/{id}", DetailAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a subscription", _Tag, "Item fields, storeSubscriptionId, labels and tags, and a bounded preview of the newest events (at most 5; the full list is .../events/enumerate). SystemAdmin or the admin token.")
                    .PathParam("id", "Subscription identifier").Bounded("AdminSubscriptionDetail", "events preview holds at most 5 events").Resp(404, "NotFound"));
            EnumerationRoutes.Add(server.Routes.PostAuthentication, baseUrl + "/{id}/events", PhaseOneCollections.SubscriptionEvents, "SubscriptionEvent",
                () => Doc("List a subscription's events", _Tag, "Newest first by default; includes raw payloads. Filter: outcome. SystemAdmin or the admin token.").Resp(404, "NotFound"),
                async (ctx, query) => await _Directory.For(ctx).AdminSubscriptions.EnumerateEventsAsync(RouteHelper.Param(ctx, "id"), query, ctx.Token).ConfigureAwait(false), "id");
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/admin/metrics/membership", MetricsAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Membership metrics", _Tag, "Totals, paid and free, by store and source, new members, estimated ARR, and lapsed members. SystemAdmin or the admin token.")
                    .Returns("MembershipMetrics"));
        }

        #endregion

        #region Private-Methods

        private async Task DetailAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).AdminSubscriptions.DetailAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task MetricsAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, await _Directory.For(ctx).AdminSubscriptions.MetricsAsync(ctx.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        #endregion
    }
}
