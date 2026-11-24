namespace TrellisKit.Core.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// External sign-in provider settings. A provider is enabled when its identifiers are set.
    /// </summary>
    public class OAuthSettings
    {
        #region Public-Members

        /// <summary>
        /// iOS bundle identifier; the audience of identity tokens from the native Sign in with Apple flow.
        /// </summary>
        public string AppleBundleId { get; set; } = String.Empty;

        /// <summary>
        /// Sign in with Apple Services ID; the audience of identity tokens from Sign in with Apple JS on the website.
        /// </summary>
        public string AppleServicesId { get; set; } = String.Empty;

        /// <summary>
        /// Google OAuth client id of type Web (also the server client id requested by the native apps).
        /// </summary>
        public string GoogleWebClientId { get; set; } = String.Empty;

        /// <summary>
        /// Google OAuth client id of type iOS.
        /// </summary>
        public string GoogleIosClientId { get; set; } = String.Empty;

        /// <summary>
        /// Google OAuth client id of type Android.
        /// </summary>
        public string GoogleAndroidClientId { get; set; } = String.Empty;

        /// <summary>
        /// Facebook app id.
        /// </summary>
        public string FacebookAppId { get; set; } = String.Empty;

        /// <summary>
        /// Facebook app secret. Secret.
        /// </summary>
        public string FacebookAppSecret { get; set; } = String.Empty;

        /// <summary>
        /// Accepted Apple audiences (bundle id and Services ID, when set).
        /// </summary>
        public IReadOnlyList<string> AppleAudiences
        {
            get { return NonEmpty(AppleBundleId, AppleServicesId); }
        }

        /// <summary>
        /// Accepted Google audiences (web, iOS, and Android client ids, when set).
        /// </summary>
        public IReadOnlyList<string> GoogleAudiences
        {
            get { return NonEmpty(GoogleWebClientId, GoogleIosClientId, GoogleAndroidClientId); }
        }

        #endregion

        #region Private-Methods

        private static IReadOnlyList<string> NonEmpty(params string[] values)
        {
            return values.Where(v => !String.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.Ordinal).ToList();
        }

        #endregion
    }
}
