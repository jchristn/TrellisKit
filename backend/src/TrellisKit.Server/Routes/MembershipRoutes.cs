namespace TrellisKit.Server.Routes
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Membership and purchases (contract section 5). Signed in; purchases also need the age attestation.
    /// </summary>
    public class MembershipRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/membership";
        private const string _Tag = "Membership";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public MembershipRoutes(PropertyDirectory directory)
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
                openApiMetadata: Doc("Read my membership", _Tag, "Membership, web offer, store product id, store account token (UUID for appAccountToken and obfuscatedAccountId), and a bounded history preview (at most 5, with totalRecords and the enumerate path). Signed in.")
                    .Bounded("Membership", "history preview holds at most 5 entries"));
            EnumerationRoutes.Add(post, _Base + "/history", PhaseOneCollections.MembershipHistory, "MembershipHistoryItem",
                () => Doc("List my membership history", _Tag, "Every entitlement of the caller, latest start first by default. Filters: source (Subscription, SignupCode, AdminComp), status (Active, Scheduled, Expired, Revoked). Signed in."),
                async (ctx, query) => await HistoryAsync(ctx, query, null).ConfigureAwait(false));
            post.Static.Add(HttpMethod.POST, _Base + "/stripe/checkout", CheckoutAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Start Stripe Checkout", _Tag, "Returns the hosted checkout URL. An optional discount sign-up code applies its discount (every invalid code returns the same message; rate limited like code validation). Signed in and age attested.")
                    .Body("{ code? }").Resp(200, "{ url }").Resp(400, "InvalidCode").Resp(409, "AlreadyMember").Resp(429, "RateLimited").Resp(503, "IntegrationNotConfigured"));
            post.Static.Add(HttpMethod.POST, _Base + "/stripe/portal", PortalAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Open the Stripe customer portal", _Tag, "Signed in and age attested.")
                    .Resp(200, "{ url }").Resp(404, "NoStripeCustomer").Resp(503, "IntegrationNotConfigured"));
            post.Static.Add(HttpMethod.POST, _Base + "/apple/verify", AppleAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Verify an App Store purchase", _Tag, "Call finishTransaction only after a 200. Signed in and age attested.")
                    .Body("{ signedTransaction }").Resp(200, "{ membership }").Resp(400, "InvalidTransaction").Resp(409, "TransactionBelongsToAnotherAccount").Resp(503, "IntegrationNotConfigured"));
            post.Static.Add(HttpMethod.POST, _Base + "/google/verify", GoogleAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Verify a Google Play purchase", _Tag, "Acknowledges the purchase. Signed in and age attested.")
                    .Body("{ purchaseToken, productId }").Resp(200, "{ membership }").Resp(400, "InvalidPurchase").Resp(409, "TransactionBelongsToAnotherAccount").Resp(503, "IntegrationNotConfigured"));
        }

        #endregion

        #region Private-Methods

        private async Task<EnumerationResult<MembershipHistoryItem>> HistoryAsync(HttpContextBase ctx, EnumerationQuery query, string? scope)
        {
            ContentActor actor = await _Directory.For(ctx).Actors.ResolveAsync(ctx, ctx.Token).ConfigureAwait(false);
            string userId = actor.RequireUserId();
            bool isStaff = actor.IsStaffFor(TargetTypeEnum.Entitlement);
            EnumerationRequest request = _Directory.For(ctx).Enumeration.Prepare(query, PhaseOneCollections.MembershipHistory, isStaff, "history|" + userId + "|" + scope);
            EnumerationResult<MembershipHistoryItem> page = await _Directory.For(ctx).Membership.EnumerateHistoryAsync(userId, request, ctx.Token).ConfigureAwait(false);
            await _Directory.For(ctx).Metadata.AttachAsync(TargetTypeEnum.Entitlement, page.Objects, h => h.Id, isStaff, ctx.Token).ConfigureAwait(false);
            return page;
        }

        private async Task GetAsync(HttpContextBase ctx)
        {
            string userId = RouteHelper.RequireUserId(ctx);
            User user = await _Directory.For(ctx).Database.Users.ReadAsync(userId, ctx.Token).ConfigureAwait(false) ?? throw new Core.Exceptions.ApiErrorException(401, "Unauthorized", "Sign in again.");
            MembershipState state = await _Directory.For(ctx).Membership.GetStateAsync(userId, ctx.Token).ConfigureAwait(false);
            Core.PropertyConfig.PropertyConfiguration p = _Directory.For(ctx).Property.Property;
            MembershipResponse response = new MembershipResponse
            {
                Membership = state.Summary,
                WebOffer = new WebOffer { PriceUsd = p.Membership.PriceUsdCents / 100m, Interval = p.Membership.Interval, ProductName = p.Membership.ProductName },
                StoreProductId = _Directory.For(ctx).Property.AppStoreProductId,
                StoreAccountToken = user.StoreAccountToken,
            };
            response.History = BoundedPreview<MembershipHistoryItem>.From(
                await HistoryAsync(ctx, new EnumerationQuery { MaxResults = BoundedPreview<MembershipHistoryItem>.MaxObjects }, "preview").ConfigureAwait(false), _Base + "/history/enumerate");
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task CheckoutAsync(HttpContextBase ctx)
        {
            string userId = RouteHelper.RequireUserId(ctx);
            SignupCodeRequest request = RouteHelper.ReadJson<SignupCodeRequest>(ctx);
            UrlResponse response = await _Directory.For(ctx).Purchases.CheckoutAsync(userId, request.Code, RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task PortalAsync(HttpContextBase ctx)
        {
            UrlResponse response = await _Directory.For(ctx).Purchases.PortalAsync(RouteHelper.RequireUserId(ctx), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task AppleAsync(HttpContextBase ctx)
        {
            string userId = RouteHelper.RequireUserId(ctx);
            AppleVerifyRequest request = RouteHelper.ReadJson<AppleVerifyRequest>(ctx);
            MembershipEnvelope response = await _Directory.For(ctx).Purchases.VerifyAppleAsync(userId, request.SignedTransaction, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task GoogleAsync(HttpContextBase ctx)
        {
            string userId = RouteHelper.RequireUserId(ctx);
            GoogleVerifyRequest request = RouteHelper.ReadJson<GoogleVerifyRequest>(ctx);
            MembershipEnvelope response = await _Directory.For(ctx).Purchases.VerifyGoogleAsync(userId, request.PurchaseToken, request.ProductId, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        #endregion
    }
}
