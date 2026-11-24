namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A request refused by a kill switch (503 FeatureDisabled) or the minimum binary version (426 ClientUpdateRequired).
    /// </summary>
    public class AppGateDecision
    {
        /// <summary>
        /// HTTP status.
        /// </summary>
        public int StatusCode { get; set; } = 503;

        /// <summary>
        /// Error code.
        /// </summary>
        public string Error { get; set; } = "FeatureDisabled";

        /// <summary>
        /// Message for members.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Feature key, or null.
        /// </summary>
        public string? Feature { get; set; } = null;

        /// <summary>
        /// Retry-After, or null.
        /// </summary>
        public int? RetryAfterSeconds { get; set; } = null;
    }
}
