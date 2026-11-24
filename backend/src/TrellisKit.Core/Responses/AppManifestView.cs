namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/app/manifest (section 11.1).
    /// </summary>
    public class AppManifestView
    {
        /// <summary>
        /// Content version.
        /// </summary>
        public long ContentVersion { get; set; } = 0;

        /// <summary>
        /// Publish time of the version, or null.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <summary>
        /// Whether the client must re-enumerate.
        /// </summary>
        public bool FullRefresh { get; set; } = false;

        /// <summary>
        /// Echo of since.
        /// </summary>
        public long? Since { get; set; } = null;

        /// <summary>
        /// Caller attributes used for evaluation.
        /// </summary>
        public Dictionary<string, object?> EvaluatedFor { get; set; } = new Dictionary<string, object?>(StringComparer.Ordinal);

        /// <summary>
        /// Resolved flags for this client (bounded by appConfig.maxFlags).
        /// </summary>
        public Dictionary<string, object?> Flags { get; set; } = new Dictionary<string, object?>(StringComparer.Ordinal);

        /// <summary>
        /// Resolved kill switches (bounded by the engine feature keys).
        /// </summary>
        public Dictionary<string, AppKillSwitchState> KillSwitches { get; set; } = new Dictionary<string, AppKillSwitchState>(StringComparer.Ordinal);

        /// <summary>
        /// Remote string delta { upserts, deletes } for the caller's client, or null when fullRefresh is true.
        /// </summary>
        public Dictionary<string, object>? Strings { get; set; } = new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>
        /// Active banners for this caller { objects, deletes }, or null when fullRefresh is true.
        /// </summary>
        public Dictionary<string, object>? Banners { get; set; } = new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>
        /// Remote asset delta { upserts, deletes }, or null when fullRefresh is true.
        /// </summary>
        public Dictionary<string, object>? Assets { get; set; } = new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>
        /// Binary policy (mobile only), or null.
        /// </summary>
        public AppBinaryPolicyView? Binary { get; set; } = null;

        /// <summary>
        /// Manifest refresh interval.
        /// </summary>
        public int CheckIntervalMinutes { get; set; } = 0;

        /// <summary>
        /// Update check interval.
        /// </summary>
        public int UpdatesCheckIntervalMinutes { get; set; } = 0;

        /// <summary>
        /// Public property configuration when since is absent, or null.
        /// </summary>
        public object? Property { get; set; } = null;
    }
}
