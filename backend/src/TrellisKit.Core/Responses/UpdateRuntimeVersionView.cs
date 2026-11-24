namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Installs per platform and runtime version (section 7.5).
    /// </summary>
    public class UpdateRuntimeVersionView
    {
        /// <summary>
        /// ios or android.
        /// </summary>
        public string Platform { get; set; } = String.Empty;

        /// <summary>
        /// Runtime version.
        /// </summary>
        public string RuntimeVersion { get; set; } = String.Empty;

        /// <summary>
        /// First seen.
        /// </summary>
        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last seen.
        /// </summary>
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Installs that checked in the last 30 days.
        /// </summary>
        public long ActiveInstalls30d { get; set; } = 0;

        /// <summary>
        /// Newest published update for this runtime version on the filtered (or production) channel, or null.
        /// </summary>
        public UpdateView? LatestUpdate { get; set; } = null;

        /// <summary>
        /// Installs by native app version (at most 10).
        /// </summary>
        public UpdateCountPreview BinaryVersions { get; set; } = new UpdateCountPreview();

        /// <summary>
        /// Labels (always empty).
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags (always empty).
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
