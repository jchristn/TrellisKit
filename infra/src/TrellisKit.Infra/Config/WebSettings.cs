namespace TrellisKit.Infra.Config
{
    using System;
    using System.Collections.Generic;
    using Amazon.CDK.AWS.CloudFront;

    /// <summary>
    /// Website distribution settings.
    /// </summary>
    public class WebSettings
    {
        private string _SpaShellPath = "/index.html";
        private List<string> _ApiPathPatterns = new List<string> { "/api/*", "/openapi.json", "/swagger*" };
        private List<string> _PrerenderedRoutes = new List<string>();
        private int _ApiOriginReadTimeoutSeconds = 60;
        private int _HstsMaxAgeSeconds = 63072000;
        private string _ContentSecurityPolicy = String.Empty;
        private string _PermissionsPolicy = "camera=(), microphone=(), geolocation=(self), payment=(self)";
        private List<string> _ManifestPathPatterns = new List<string> { "/api/v1/updates/manifest", "/api/v1/app/manifest" };
        private string _EmbedPathPattern = "/embed/assistants/*";

        /// <summary>
        /// Object served for client-rendered routes such as /app/* and /admin/*. Default /index.html.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string SpaShellPath
        {
            get { return _SpaShellPath; }
            set { _SpaShellPath = Guard.NotEmpty(value, nameof(SpaShellPath)); }
        }

        /// <summary>
        /// Path patterns forwarded to the API origin, uncached, with every header (except Host), cookie, and query string.
        /// Default /api/*, /openapi.json, /swagger*.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public List<string> ApiPathPatterns
        {
            get { return _ApiPathPatterns; }
            set { _ApiPathPatterns = Guard.NotNull(value, nameof(ApiPathPatterns)); }
        }

        /// <summary>
        /// Marketing routes prerendered at build time as {route}/index.html, for example /about. Requests for these
        /// paths are served the prerendered page; every other extensionless path gets the SPA shell. Default empty
        /// (until the website build emits prerendered pages).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public List<string> PrerenderedRoutes
        {
            get { return _PrerenderedRoutes; }
            set { _PrerenderedRoutes = Guard.NotNull(value, nameof(PrerenderedRoutes)); }
        }

        /// <summary>
        /// Time CloudFront waits for the API origin to respond. Default 60. Minimum 1, maximum 180
        /// (values over 60 need a CloudFront quota increase).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int ApiOriginReadTimeoutSeconds
        {
            get { return _ApiOriginReadTimeoutSeconds; }
            set { _ApiOriginReadTimeoutSeconds = Guard.Range(value, 1, 180, nameof(ApiOriginReadTimeoutSeconds)); }
        }

        /// <summary>
        /// Strict-Transport-Security max-age. Default 63072000 (two years). Minimum 0, maximum 63072000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int HstsMaxAgeSeconds
        {
            get { return _HstsMaxAgeSeconds; }
            set { _HstsMaxAgeSeconds = Guard.Range(value, 0, 63072000, nameof(HstsMaxAgeSeconds)); }
        }

        /// <summary>
        /// Add the preload directive to Strict-Transport-Security. Default false. Only enable once every subdomain is HTTPS.
        /// </summary>
        public bool HstsPreload { get; set; } = false;

        /// <summary>
        /// Content-Security-Policy header value. Default empty (header not sent) until the website's third-party
        /// origins (Stripe, Apple, Google, Facebook) are finalized.
        /// </summary>
        public string ContentSecurityPolicy
        {
            get { return _ContentSecurityPolicy; }
            set { _ContentSecurityPolicy = value ?? String.Empty; }
        }

        /// <summary>
        /// Permissions-Policy header value. Default camera=(), microphone=(), geolocation=(self), payment=(self).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string PermissionsPolicy
        {
            get { return _PermissionsPolicy; }
            set { _PermissionsPolicy = Guard.NotEmpty(value, nameof(PermissionsPolicy)); }
        }

        /// <summary>
        /// Live update and app configuration manifest paths (docs/LIVE_UPDATES_CONTRACT.md section 17). Each gets its own
        /// API behavior, ordered before the API path patterns, that never caches and forwards every viewer header except
        /// Host (expo-*, X-TK-*, X-Client*, Accept, If-None-Match, Authorization) plus the CloudFront viewer headers
        /// (CloudFront-Viewer-Country for targeting, CloudFront-Viewer-Address for per-IP limits). Default
        /// /api/v1/updates/manifest, /api/v1/app/manifest.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public List<string> ManifestPathPatterns
        {
            get { return _ManifestPathPatterns; }
            set { _ManifestPathPatterns = Guard.NotNull(value, nameof(ManifestPathPatterns)); }
        }

        /// <summary>
        /// Path pattern of the assistant embed page (docs/AI_CONTRACT.md section 13.3). It is served like any other
        /// client-rendered route but with Content-Security-Policy frame-ancestors (AiSettings.FrameAncestorsSources)
        /// instead of X-Frame-Options DENY. Default /embed/assistants/*.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string EmbedPathPattern
        {
            get { return _EmbedPathPattern; }
            set { _EmbedPathPattern = Guard.NotEmpty(value, nameof(EmbedPathPattern)); }
        }

        /// <summary>
        /// CloudFront edge locations used by the website and redirect distributions. Default PRICE_CLASS_100.
        /// </summary>
        public PriceClass PriceClass { get; set; } = PriceClass.PRICE_CLASS_100;
    }
}
