namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// Mobile app identity.
    /// </summary>
    public class PropertyMobile
    {
        #region Private-Members

        private PropertyMobileNavigation? _Navigation = null;

        #endregion

        #region Public-Members

        /// <summary>
        /// Optional mobile drawer order and bottom shortcuts. Null retains the bundled or engine defaults.
        /// </summary>
        public PropertyMobileNavigation? Navigation
        {
            get => _Navigation;
            set => _Navigation = value;
        }


        /// <summary>
        /// iOS bundle identifier.
        /// </summary>
        public string BundleId { get; set; } = String.Empty;

        /// <summary>
        /// Android package name.
        /// </summary>
        public string AndroidPackage { get; set; } = String.Empty;

        /// <summary>
        /// Deep link URL scheme.
        /// </summary>
        public string Scheme { get; set; } = String.Empty;

        /// <summary>
        /// Expo EAS project identifier, or empty.
        /// </summary>
        public string EasProjectId { get; set; } = String.Empty;

        /// <summary>
        /// Minimum native app version: one semver string for both platforms or { "ios": "1.2.0", "android": "1.2.0" }.
        /// Null means no minimum (docs/LIVE_UPDATES_CONTRACT.md section 15).
        /// </summary>
        public JsonElement? MinimumBinaryVersion { get; set; } = null;

        /// <summary>
        /// Latest native app version, same shape as <see cref="MinimumBinaryVersion"/>; older binaries see a dismissible
        /// "new version available" hint. Null means none.
        /// </summary>
        public JsonElement? LatestBinaryVersion { get; set; } = null;

        /// <summary>
        /// Store links by platform ("ios", "android"), or null.
        /// </summary>
        public Dictionary<string, string>? StoreUrls { get; set; } = null;

        /// <summary>
        /// Apple numeric App Store id, or null.
        /// </summary>
        public string? AppStoreId { get; set; } = null;

        /// <summary>
        /// Apple Developer Team ID (10 characters, public; it prefixes the app id in signed apps), or null. When set
        /// with the bundle id, the website serves /.well-known/apple-app-site-association so iOS offers to save and
        /// autofill the app's passwords for the website domain (webcredentials).
        /// </summary>
        public string? AppleTeamId { get; set; } = null;

        /// <summary>
        /// SHA-256 fingerprints (colon-separated hex) of the Android signing certificates, for example the Play app
        /// signing key and the upload key, or null. When set, the website serves /.well-known/assetlinks.json so Android
        /// shares saved passwords between the app and the website.
        /// </summary>
        public List<string>? AndroidCertFingerprints { get; set; } = null;

        /// <summary>
        /// Google Maps Android key used at build time when the directory map provider is PlatformNative, or null.
        /// A client-visible key restricted at the provider by app id; never read from server settings.
        /// </summary>
        public string? GoogleMapsApiKey { get; set; } = null;

        #endregion
    }
}
