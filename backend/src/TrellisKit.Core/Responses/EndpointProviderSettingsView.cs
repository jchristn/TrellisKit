namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Provider settings of an endpoint.
    /// </summary>
    public class EndpointProviderSettingsView
    {
        #region Public-Members

        /// <summary>
        /// Region.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// Project.
        /// </summary>
        public string? Project { get; set; } = null;

        /// <summary>
        /// API version.
        /// </summary>
        public string? ApiVersion { get; set; } = null;

        /// <summary>
        /// Deployment.
        /// </summary>
        public string? Deployment { get; set; } = null;

        /// <summary>
        /// Access key id (not secret).
        /// </summary>
        public string? AccessKeyId { get; set; } = null;

        #endregion
    }
}
