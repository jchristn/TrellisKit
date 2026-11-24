namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Public website settings used to build links in emails and redirects.
    /// </summary>
    public class WebSettings
    {
        #region Public-Members

        /// <summary>
        /// Public website origin, for example https://example.test. Default empty: https:// followed by the property
        /// domains.web.
        /// </summary>
        public string PublicBaseUrl { get; set; } = String.Empty;

        #endregion
    }
}
