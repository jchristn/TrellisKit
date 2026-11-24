namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Member sign-up code routes (website only; contract section 6). Signed in and age attested.
    /// </summary>
    public class SignupCodeRoutes
    {
        #region Private-Members

        private const string _Base = Constants.ApiPrefix + "/signup-codes";
        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public SignupCodeRoutes(PropertyDirectory directory)
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
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, _Base + "/validate", ValidateAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Check a sign-up code", "Sign-up codes", "Describes the grant without redeeming. Every invalid code returns the same message. Rate limited per IP and per account. Signed in.")
                    .Body("{ code }").Resp(200, "{ valid, kind (FreeMembership or Discount), name, description, grantSummary, membershipEndsUtc, note?, discountSummary? } or { valid: false, message }").Resp(429, "RateLimited"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, _Base + "/redeem", RedeemAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Redeem a sign-up code", "Sign-up codes", "Grants the free membership in one transaction. A discount code is not redeemed here: send it with Stripe checkout (400 DiscountCodeAtCheckout). Signed in.")
                    .Body("{ code }").Resp(200, "{ membership, grantSummary, membershipEndsUtc }").Resp(400, "InvalidCode or DiscountCodeAtCheckout").Resp(409, "AlreadyRedeemed").Resp(429, "RateLimited"));
        }

        #endregion

        #region Private-Methods

        private async Task ValidateAsync(HttpContextBase ctx)
        {
            string userId = RouteHelper.RequireUserId(ctx);
            SignupCodeRequest request = RouteHelper.ReadJson<SignupCodeRequest>(ctx);
            SignupCodeValidationResponse response = await _Directory.For(ctx).SignupCodes.ValidateAsync(userId, request.Code, RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task RedeemAsync(HttpContextBase ctx)
        {
            string userId = RouteHelper.RequireUserId(ctx);
            SignupCodeRequest request = RouteHelper.ReadJson<SignupCodeRequest>(ctx);
            SignupCodeRedeemResponse response = await _Directory.For(ctx).SignupCodes.RedeemAsync(userId, request.Code, RequestInfo.From(ctx, _Directory.For(ctx).Settings.Rest), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        #endregion
    }
}
