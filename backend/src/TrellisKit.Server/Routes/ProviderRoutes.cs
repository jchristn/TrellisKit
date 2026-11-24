namespace TrellisKit.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// GET /api/v1/auth/providers: which sign-in methods and stores are enabled, with the public identifiers clients need.
    /// </summary>
    public class ProviderRoutes
    {
        #region Private-Members

        private readonly PropertyDirectory _Directory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public ProviderRoutes(PropertyDirectory directory)
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
            server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/auth/providers", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Available sign-in methods and stores", "Auth", "Which providers and stores are enabled and their public client identifiers (never secrets). In the Open access model stripe, appleIap, and googlePlay are always disabled. Anonymous.")
                    .Returns("Providers"));
        }

        /// <summary>
        /// Build the providers response for the property a request resolved to.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Response.</returns>
        public ProvidersResponse Build(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            Core.Settings.AppSettings s = services.Settings;
            // The Open access model sells nothing: every store reports disabled (docs/ACCESS_MODELS_CONTRACT.md section 4).
            bool purchases = services.Access.Current.PurchasesEnabled;
            return new ProvidersResponse
            {
                Email = true,
                Apple = new AppleProviderInfo { Enabled = services.Integrations.Apple.IsConfigured, WebClientId = Null(s.OAuth.AppleServicesId) },
                Google = new GoogleProviderInfo
                {
                    Enabled = services.Integrations.Google.IsConfigured,
                    WebClientId = Null(s.OAuth.GoogleWebClientId),
                    IosClientId = Null(s.OAuth.GoogleIosClientId),
                    AndroidClientId = Null(s.OAuth.GoogleAndroidClientId)
                },
                Facebook = new FacebookProviderInfo { Enabled = services.Integrations.Facebook.IsConfigured, AppId = Null(s.OAuth.FacebookAppId) },
                Stripe = new ProviderToggle { Enabled = purchases && services.Integrations.Stripe.IsConfigured && !String.IsNullOrWhiteSpace(s.Stripe.PriceId) },
                AppleIap = new StoreProductInfo { Enabled = purchases && services.Integrations.AppStore.IsConfigured, ProductId = services.Property.AppStoreProductId },
                GooglePlay = new StoreProductInfo { Enabled = purchases && services.Integrations.GooglePlay.IsConfigured, ProductId = services.Property.GooglePlayProductId }
            };
        }

        #endregion

        #region Private-Methods

        private async Task GetAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, Build(ctx)).ConfigureAwait(false);
        }

        private static string? Null(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
