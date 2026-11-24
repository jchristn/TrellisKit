namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The protocol inputs of one Expo Updates manifest request (docs/LIVE_UPDATES_CONTRACT.md section 3.1).
    /// </summary>
    public class UpdateManifestRequest
    {
        /// <summary>
        /// expo-protocol-version (0 when absent).
        /// </summary>
        public int ProtocolVersion { get; set; } = 0;

        /// <summary>
        /// expo-platform.
        /// </summary>
        public string? Platform { get; set; } = null;

        /// <summary>
        /// expo-runtime-version.
        /// </summary>
        public string? RuntimeVersion { get; set; } = null;

        /// <summary>
        /// expo-channel-name.
        /// </summary>
        public string? Channel { get; set; } = null;

        /// <summary>
        /// expo-current-update-id.
        /// </summary>
        public string? CurrentUpdateId { get; set; } = null;

        /// <summary>
        /// expo-embedded-update-id.
        /// </summary>
        public string? EmbeddedUpdateId { get; set; } = null;

        /// <summary>
        /// expo-expect-signature (structured dictionary).
        /// </summary>
        public string? ExpectSignature { get; set; } = null;

        /// <summary>
        /// expo-recent-failed-update-ids (structured list).
        /// </summary>
        public string? RecentFailedUpdateIds { get; set; } = null;

        /// <summary>
        /// expo-extra-params (structured dictionary).
        /// </summary>
        public string? ExtraParams { get; set; } = null;

        /// <summary>
        /// X-TK-Install-Id.
        /// </summary>
        public string? InstallId { get; set; } = null;

        /// <summary>
        /// X-Client-Version.
        /// </summary>
        public string? AppVersion { get; set; } = null;

        /// <summary>
        /// Accept.
        /// </summary>
        public string? Accept { get; set; } = null;

        /// <summary>
        /// Origin used for asset URLs served by this API.
        /// </summary>
        public string AssetOrigin { get; set; } = String.Empty;

        /// <summary>
        /// Client IP.
        /// </summary>
        public string? SourceIp { get; set; } = null;
    }
}
