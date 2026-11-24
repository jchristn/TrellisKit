namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A mobile or tablet app registered to a property. Platform and app id are unique across the deployment.
    /// </summary>
    public class PropertyApp
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ppa_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Owning property.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Platform.
        /// </summary>
        public AppPlatformEnum Platform { get; set; } = AppPlatformEnum.Ios;

        /// <summary>
        /// iOS bundle id or Android package name, sent by the app as X-TK-App.
        /// </summary>
        public string AppId { get; set; } = String.Empty;

        /// <summary>
        /// Store app id (App Store app Apple id), or null.
        /// </summary>
        public string? StoreAppId { get; set; } = null;

        /// <summary>
        /// Whether requests from this app are accepted.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last change time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
