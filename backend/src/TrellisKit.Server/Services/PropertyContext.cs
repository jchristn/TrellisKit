namespace TrellisKit.Server.Services
{
    using System;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// The active property together with the running settings, resolving values that the property supplies by
    /// default and that settings may override (public website URL, email sender, store product ids).
    /// Thread safety: reads the live settings instance on every call; safe to share.
    /// </summary>
    public class PropertyContext
    {
        #region Public-Members

        /// <summary>
        /// Active property.
        /// </summary>
        public PropertyConfiguration Property { get; }

        /// <summary>
        /// Running settings.
        /// </summary>
        public AppSettings Settings { get; }

        /// <summary>
        /// Product (property) display name.
        /// </summary>
        public string Name
        {
            get { return Property.Name; }
        }

        /// <summary>
        /// Public website origin without a trailing slash: web.publicBaseUrl when set, otherwise https:// followed by
        /// the property domains.web.
        /// </summary>
        public string PublicBaseUrl
        {
            get
            {
                string configured = (Settings.Web.PublicBaseUrl ?? String.Empty).Trim();
                if (configured.Length > 0) return configured.TrimEnd('/');
                return "https://" + (Property.Domains.Web ?? String.Empty).Trim().TrimEnd('/');
            }
        }

        /// <summary>
        /// Public API origin without a trailing slash, for absolute API links (calendar feeds, ICS links, email images). See
        /// <see cref="ApiOriginFor"/>.
        /// </summary>
        public string ApiOrigin
        {
            get { return ApiOriginFor(PublicBaseUrl, Property.Domains.Api); }
        }

        /// <summary>
        /// Email sender display name: email.fromName when set, otherwise the property contact.emailFromName.
        /// </summary>
        public string EmailFromName
        {
            get { return Pick(Settings.Email.FromName, Property.Contact.EmailFromName); }
        }

        /// <summary>
        /// Email sender address: email.fromAddress when set, otherwise the property contact.emailFromAddress.
        /// </summary>
        public string EmailFromAddress
        {
            get { return Pick(Settings.Email.FromAddress, Property.Contact.EmailFromAddress); }
        }

        /// <summary>
        /// App Store product id: appStore.productId when set, otherwise the property membership.storeProductId.
        /// </summary>
        public string AppStoreProductId
        {
            get { return Pick(Settings.AppStore.ProductId, Property.Membership.StoreProductId); }
        }

        /// <summary>
        /// Google Play product id: googlePlay.productId when set, otherwise the property membership.storeProductId.
        /// </summary>
        public string GooglePlayProductId
        {
            get { return Pick(Settings.GooglePlay.ProductId, Property.Membership.StoreProductId); }
        }

        /// <summary>
        /// Supplies the property's current access configuration (set by the service graph to
        /// AccessPolicyService.Current), or null to use the property document's access section.
        /// </summary>
        public Func<TrellisKit.Core.Models.AccessPolicy>? AccessSource { get; set; } = null;

        /// <summary>
        /// The property's current access configuration (docs/ACCESS_MODELS_CONTRACT.md). Never null.
        /// </summary>
        public TrellisKit.Core.Models.AccessPolicy Access
        {
            get { return AccessSource?.Invoke() ?? TrellisKit.Core.Models.AccessPolicy.FromDocument(Property.Access); }
        }

        /// <summary>
        /// Supplies the property's current brand palette (set by the service graph to BrandPaletteService.Current), or
        /// null to use the property document's brand.colors.
        /// </summary>
        public Func<TrellisKit.Core.Models.BrandPalette>? BrandSource { get; set; } = null;

        /// <summary>
        /// The property's effective brand colors (docs/TRELLISKIT.md section 2.1): the stored palette, every key set. Never null.
        /// </summary>
        public PropertyBrandColors BrandColors
        {
            get { return (BrandSource?.Invoke() ?? TrellisKit.Core.Models.BrandPalette.FromDocument(Property.Brand?.Colors)).ToDocument(); }
        }

        /// <summary>
        /// Minimum member age in years, from the property.
        /// </summary>
        public int MinimumAge
        {
            get { return Property.Membership.MinimumAge; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the context.
        /// </summary>
        /// <param name="property">Active property.</param>
        /// <param name="settings">Running settings (live instance).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PropertyContext(PropertyConfiguration property, AppSettings settings)
        {
            Property = property ?? throw new ArgumentNullException(nameof(property));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Absolute website link.
        /// </summary>
        /// <param name="pathAndQuery">Path and query starting with a slash, for example /app/membership.</param>
        /// <returns>Absolute URL.</returns>
        public string Link(string pathAndQuery)
        {
            return PublicBaseUrl + (pathAndQuery ?? String.Empty);
        }

        /// <summary>
        /// The public API origin: the website origin when it is a loopback host (a local stack, where the website serves /api
        /// through its proxy) or when no API domain is configured, otherwise https:// followed by the API domain.
        /// </summary>
        /// <param name="publicBaseUrl">Website origin (web.publicBaseUrl or https:// plus domains.web).</param>
        /// <param name="apiDomain">The property's domains.api, or null.</param>
        /// <returns>Origin without a trailing slash.</returns>
        public static string ApiOriginFor(string publicBaseUrl, string? apiDomain)
        {
            string web = (publicBaseUrl ?? String.Empty).Trim().TrimEnd('/');
            string api = (apiDomain ?? String.Empty).Trim().TrimEnd('/');
            if (api.Length == 0) return web;
            if (Uri.TryCreate(web, UriKind.Absolute, out Uri? uri) && uri.IsLoopback) return web;
            return "https://" + api;
        }

        #endregion

        #region Private-Methods

        private static string Pick(string? configured, string? fallback)
        {
            return String.IsNullOrWhiteSpace(configured) ? (fallback ?? String.Empty).Trim() : configured.Trim();
        }

        #endregion
    }
}
