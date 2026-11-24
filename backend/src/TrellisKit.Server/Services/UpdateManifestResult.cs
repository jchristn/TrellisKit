namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A manifest endpoint response: status, headers, and body bytes, written as is.
    /// </summary>
    public class UpdateManifestResult
    {
        /// <summary>
        /// HTTP status.
        /// </summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = "text/plain";

        /// <summary>
        /// Response headers.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Body.
        /// </summary>
        public byte[] Body { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Telemetry outcome (update, no_update, rollback_directive, not_in_rollout, error).
        /// </summary>
        public string Outcome { get; set; } = "error";
    }
}
