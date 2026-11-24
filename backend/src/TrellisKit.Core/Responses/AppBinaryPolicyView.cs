namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Binary version policy resolved for the caller (section 15.2).
    /// </summary>
    public class AppBinaryPolicyView
    {
        /// <summary>
        /// Minimum version for the caller's platform, or null.
        /// </summary>
        public string? MinimumVersion { get; set; } = null;

        /// <summary>
        /// Latest version for the caller's platform, or null.
        /// </summary>
        public string? LatestVersion { get; set; } = null;

        /// <summary>
        /// Below the minimum.
        /// </summary>
        public bool UpdateRequired { get; set; } = false;

        /// <summary>
        /// Below the latest.
        /// </summary>
        public bool UpdateRecommended { get; set; } = false;

        /// <summary>
        /// Store link, or null.
        /// </summary>
        public string? StoreUrl { get; set; } = null;
    }
}
