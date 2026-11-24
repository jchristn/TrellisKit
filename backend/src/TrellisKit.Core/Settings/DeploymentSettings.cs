namespace TrellisKit.Core.Settings
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Deployment mode and property resolution settings (docs/TRELLISKIT.md section 10, docs/MULTIPROPERTY_CONTRACT.md
    /// sections 1, 2, and 12).
    /// </summary>
    public class DeploymentSettings
    {
        #region Public-Members

        /// <summary>
        /// Deployment mode. Default SingleProperty. Restart.
        /// </summary>
        public DeploymentModeEnum Mode { get; set; } = DeploymentModeEnum.SingleProperty;

        /// <summary>
        /// Deployment name, the trelliskit.deployment telemetry resource attribute in MultiProperty. Default
        /// "trelliskit". 1 to 64 characters.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null or empty.</exception>
        public string Name
        {
            get { return _Name; }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name));
                string trimmed = value.Trim();
                _Name = trimmed.Length > 64 ? trimmed.Substring(0, 64) : trimmed;
            }
        }

        /// <summary>
        /// Whether property resolution uses X-Forwarded-Host (set by nginx and CloudFront) instead of Host. Default true.
        /// </summary>
        public bool TrustForwardedHost { get; set; } = true;

        /// <summary>
        /// In MultiProperty, whether a request with X-Client: mobile must carry X-TK-App. Default true.
        /// </summary>
        public bool RequireAppHeader { get; set; } = true;

        /// <summary>
        /// Seconds between registry change checks (a change made on one instance reaches the others within this
        /// interval). Default 30. Minimum 1, maximum 600.
        /// </summary>
        public int PropertyCacheSeconds
        {
            get { return _PropertyCacheSeconds; }
            set { _PropertyCacheSeconds = Math.Clamp(value, 1, 600); }
        }

        /// <summary>
        /// Maximum number of properties (apps) in one deployment, not counting purged tombstones. Default 25 (the load
        /// balancer certificate default). Minimum 1, maximum 25.
        /// </summary>
        public int MaxProperties
        {
            get { return _MaxProperties; }
            set { _MaxProperties = Math.Clamp(value, 1, MaxPropertiesLimit); }
        }

        /// <summary>
        /// Hard upper bound of <see cref="MaxProperties"/>.
        /// </summary>
        public const int MaxPropertiesLimit = 25;

        /// <summary>
        /// DNS-over-HTTPS JSON endpoint used to read TXT records when a property administrator verifies a domain
        /// (application/dns-json, for example https://cloudflare-dns.com/dns-query). Default Cloudflare.
        /// </summary>
        public string DomainVerificationResolverUrl
        {
            get { return _DomainVerificationResolverUrl; }
            set { _DomainVerificationResolverUrl = String.IsNullOrWhiteSpace(value) ? DefaultDomainVerificationResolverUrl : value.Trim(); }
        }

        /// <summary>
        /// Default DNS-over-HTTPS endpoint.
        /// </summary>
        public const string DefaultDomainVerificationResolverUrl = "https://cloudflare-dns.com/dns-query";

        #endregion

        #region Private-Members

        private string _DomainVerificationResolverUrl = DefaultDomainVerificationResolverUrl;

        private string _Name = "trelliskit";
        private int _PropertyCacheSeconds = 30;
        private int _MaxProperties = 25;

        #endregion
    }
}
