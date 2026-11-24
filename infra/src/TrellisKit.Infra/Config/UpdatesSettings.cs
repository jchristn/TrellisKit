namespace TrellisKit.Infra.Config
{
    using System;

    /// <summary>
    /// Self-hosted live updates (Expo Updates protocol): the updates bucket and its CloudFront paths on the media host.
    /// Object keys in the bucket mirror the URL path, so a hashed asset served at /updates/assets/{sha256} is stored
    /// under the key updates/assets/{sha256}.
    /// </summary>
    public class UpdatesSettings
    {
        private string _PathPattern = "/updates/*";
        private string _AssetsPathPattern = "/updates/assets/*";
        private string _PathPrefix = "/updates";
        private int _ImmutableMaxAgeSeconds = 31536000;

        /// <summary>
        /// Path pattern of every updates object on the media host. Served from the updates bucket without signed cookies
        /// and never cached (manifests and any other mutable document). Default /updates/*.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string PathPattern
        {
            get { return _PathPattern; }
            set { _PathPattern = Guard.NotEmpty(value, nameof(PathPattern)); }
        }

        /// <summary>
        /// Path pattern of content-addressed (hashed) bundles and assets. Cached immutably at the edge and in clients.
        /// Must be more specific than <see cref="PathPattern"/>. Default /updates/assets/*.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string AssetsPathPattern
        {
            get { return _AssetsPathPattern; }
            set { _AssetsPathPattern = Guard.NotEmpty(value, nameof(AssetsPathPattern)); }
        }

        /// <summary>
        /// URL path (no trailing slash) appended to the media host to form the updates public base URL. Default /updates.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string PathPrefix
        {
            get { return _PathPrefix; }
            set { _PathPrefix = Guard.NotEmpty(value, nameof(PathPrefix)).TrimEnd('/'); }
        }

        /// <summary>
        /// Edge and browser cache lifetime of hashed assets, sent as Cache-Control max-age with immutable. Default
        /// 31536000 (one year). Minimum 86400, maximum 31536000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int ImmutableMaxAgeSeconds
        {
            get { return _ImmutableMaxAgeSeconds; }
            set { _ImmutableMaxAgeSeconds = Guard.Range(value, 86400, 31536000, nameof(ImmutableMaxAgeSeconds)); }
        }
    }
}
