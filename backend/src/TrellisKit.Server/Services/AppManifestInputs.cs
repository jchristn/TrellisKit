namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Caller inputs of GET /api/v1/app/manifest (section 11.1).
    /// </summary>
    public class AppManifestInputs
    {
        /// <summary>
        /// Cached content version, or null.
        /// </summary>
        public long? Since { get; set; } = null;

        /// <summary>
        /// web or mobile.
        /// </summary>
        public string Client { get; set; } = "web";

        /// <summary>
        /// X-Client-Platform.
        /// </summary>
        public string? Platform { get; set; } = null;

        /// <summary>
        /// X-Client-Version.
        /// </summary>
        public string? AppVersion { get; set; } = null;

        /// <summary>
        /// X-Client-Update-Id.
        /// </summary>
        public string? UpdateId { get; set; } = null;

        /// <summary>
        /// X-Client-Channel.
        /// </summary>
        public string? Channel { get; set; } = null;

        /// <summary>
        /// X-TK-Install-Id or X-TK-Visitor-Id.
        /// </summary>
        public string? ClientId { get; set; } = null;

        /// <summary>
        /// Bearer access token, or null.
        /// </summary>
        public string? AccessToken { get; set; } = null;

        /// <summary>
        /// CloudFront-Viewer-Country, or null.
        /// </summary>
        public string? Country { get; set; } = null;

        /// <summary>
        /// Client IP.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// Public property document to include, or null.
        /// </summary>
        public object? Property { get; set; } = null;

        /// <summary>
        /// Request origin (absolute URLs of locally served content assets).
        /// </summary>
        public string Origin { get; set; } = String.Empty;
    }
}
