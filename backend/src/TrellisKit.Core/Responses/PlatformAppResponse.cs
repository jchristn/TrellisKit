namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A property app in a platform payload.
    /// </summary>
    public class PlatformAppResponse : CatalogEntryMetadata
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ppa_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Platform.
        /// </summary>
        public AppPlatformEnum Platform { get; set; } = AppPlatformEnum.Ios;

        /// <summary>
        /// Bundle id or package.
        /// </summary>
        public string AppId { get; set; } = String.Empty;

        /// <summary>
        /// Store app id.
        /// </summary>
        public string? StoreAppId { get; set; } = null;

        /// <summary>
        /// Whether requests from the app are accepted.
        /// </summary>
        public bool Enabled { get; set; } = true;

        #endregion
    }
}
