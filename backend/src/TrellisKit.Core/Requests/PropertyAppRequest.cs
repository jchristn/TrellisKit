namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An app in a property create or update body.
    /// </summary>
    public class PropertyAppRequest
    {
        #region Public-Members

        /// <summary>
        /// Platform.
        /// </summary>
        public AppPlatformEnum Platform { get; set; } = AppPlatformEnum.Ios;

        /// <summary>
        /// iOS bundle id or Android package.
        /// </summary>
        public string AppId { get; set; } = String.Empty;

        /// <summary>
        /// Store app id, or null.
        /// </summary>
        public string? StoreAppId { get; set; } = null;

        /// <summary>
        /// Whether requests from the app are accepted.
        /// </summary>
        public bool Enabled { get; set; } = true;

        #endregion
    }
}
