namespace TrellisKit.Infra.Config
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// DNS names served by one environment.
    /// </summary>
    public class DomainSettings
    {
        private string _PrimaryDomain = "example.test";
        private List<string> _RedirectDomains = new List<string>();
        private string _RedirectTargetUrl = "https://example.test";
        private string _ApiLabel = "api";
        private bool _ApiOnWebsiteHost = false;
        private string _MediaLabel = "media";
        private string _WwwLabel = "www";

        /// <summary>
        /// Apex of the primary website zone, from the property's domains.web (prefixed with dev. in development).
        /// Default example.test. A Route 53 hosted zone is created for it.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string PrimaryDomain
        {
            get { return _PrimaryDomain; }
            set { _PrimaryDomain = Guard.NotEmpty(value, nameof(PrimaryDomain)); }
        }

        /// <summary>
        /// Vanity apexes that permanently redirect to the website, from the property's domains.redirects. Default
        /// empty. Each gets a hosted zone; when the list is empty no redirect stack is created.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public List<string> RedirectDomains
        {
            get { return _RedirectDomains; }
            set { _RedirectDomains = Guard.NotNull(value, nameof(RedirectDomains)); }
        }

        /// <summary>
        /// Absolute URL (scheme and host, no trailing slash) the vanity domain redirects to. Default https://example.test.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string RedirectTargetUrl
        {
            get { return _RedirectTargetUrl; }
            set { _RedirectTargetUrl = Guard.NotEmpty(value, nameof(RedirectTargetUrl)).TrimEnd('/'); }
        }

        /// <summary>
        /// Host label of the API load-balancer origin, prepended to the primary domain. Default api.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string ApiLabel
        {
            get { return _ApiLabel; }
            set { _ApiLabel = Guard.NotEmpty(value, nameof(ApiLabel)); }
        }

        /// <summary>
        /// Host label of the media CDN, prepended to the primary domain. Default media.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string MediaLabel
        {
            get { return _MediaLabel; }
            set { _MediaLabel = Guard.NotEmpty(value, nameof(MediaLabel)); }
        }

        /// <summary>
        /// Host label of the www alias on both zones. Default www.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string WwwLabel
        {
            get { return _WwwLabel; }
            set { _WwwLabel = Guard.NotEmpty(value, nameof(WwwLabel)); }
        }

        /// <summary>
        /// Fully qualified API host, for example api.example.test.
        /// </summary>
        public string ApiHost
        {
            get { return _ApiOnWebsiteHost ? _PrimaryDomain : ApiOriginHost; }
        }

        /// <summary>
        /// Whether the public API shares the website host and is routed through CloudFront. Default false.
        /// </summary>
        public bool ApiOnWebsiteHost
        {
            get { return _ApiOnWebsiteHost; }
            set { _ApiOnWebsiteHost = value; }
        }

        /// <summary>
        /// Dedicated API load-balancer host used for DNS, its TLS certificate, and the website's API origin.
        /// </summary>
        public string ApiOriginHost
        {
            get { return _ApiLabel + "." + _PrimaryDomain; }
        }

        /// <summary>
        /// Fully qualified media host, for example media.example.test.
        /// </summary>
        public string MediaHost
        {
            get { return _MediaLabel + "." + _PrimaryDomain; }
        }

        /// <summary>
        /// Fully qualified www host of the website, for example www.example.test.
        /// </summary>
        public string WwwHost
        {
            get { return _WwwLabel + "." + _PrimaryDomain; }
        }

        /// <summary>
        /// Fully qualified www host of a vanity domain, for example www.example-vanity.test.
        /// </summary>
        /// <param name="redirectDomain">Vanity apex.</param>
        /// <returns>Host.</returns>
        /// <exception cref="ArgumentNullException">The domain is empty.</exception>
        public string RedirectWwwHost(string redirectDomain)
        {
            return _WwwLabel + "." + Guard.NotEmpty(redirectDomain, nameof(redirectDomain));
        }

        /// <summary>
        /// Public website origin, for example https://example.test.
        /// </summary>
        public string WebsiteUrl
        {
            get { return "https://" + _PrimaryDomain; }
        }

        /// <summary>
        /// Public API origin, for example https://api.example.test.
        /// </summary>
        public string ApiUrl
        {
            get { return "https://" + ApiHost; }
        }
    }
}
