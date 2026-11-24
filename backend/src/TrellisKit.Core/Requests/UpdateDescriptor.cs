namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The update part of POST /api/v1/admin/updates (docs/LIVE_UPDATES_CONTRACT.md section 10.3 with the client implementation notes).
    /// </summary>
    public class UpdateDescriptor
    {
        /// <summary>
        /// ios or android.
        /// </summary>
        public string Platform { get; set; } = String.Empty;

        /// <summary>
        /// Fingerprint runtime version (1 to 128 characters).
        /// </summary>
        public string RuntimeVersion { get; set; } = String.Empty;

        /// <summary>
        /// Target channel.
        /// </summary>
        public string Channel { get; set; } = String.Empty;

        /// <summary>
        /// Release message (1 to 500 characters).
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Critical update.
        /// </summary>
        public bool Critical { get; set; } = false;

        /// <summary>
        /// Critical prompt text (up to 200 characters; implies critical).
        /// </summary>
        public string? CriticalMessage { get; set; } = null;

        /// <summary>
        /// Initial rollout 0 to 100 (two decimals); 0 stores a Draft. Null: 100 for preview and development, 10 for production.
        /// </summary>
        public decimal? RolloutPercentage { get; set; } = null;

        /// <summary>
        /// Git commit (7 to 40 hex characters), or null.
        /// </summary>
        public string? GitCommit { get; set; } = null;

        /// <summary>
        /// Whether the working tree had uncommitted changes.
        /// </summary>
        public bool GitDirty { get; set; } = false;

        /// <summary>
        /// Client export time, or null.
        /// </summary>
        public DateTime? CreatedUtc { get; set; } = null;

        /// <summary>
        /// UUID shared by the platforms of one publish run, or null to group by channel, commit, and message.
        /// </summary>
        public string? UpdateGroupId { get; set; } = null;

        /// <summary>
        /// Complete an older update still mid-rollout on the same target instead of failing with RolloutConflict.
        /// </summary>
        public bool Supersede { get; set; } = false;

        /// <summary>
        /// Public Expo config (served as extra.expoClient), at most 256 KB.
        /// </summary>
        public JsonElement? ExpoConfig { get; set; } = null;

        /// <summary>
        /// The JavaScript bundle.
        /// </summary>
        public UpdateDescriptorFile? LaunchAsset { get; set; } = null;

        /// <summary>
        /// Every other asset.
        /// </summary>
        public List<UpdateDescriptorFile> Assets { get; set; } = new List<UpdateDescriptorFile>();

        /// <summary>
        /// Private source maps (stored, never served).
        /// </summary>
        public List<UpdateDescriptorFile> SourceMaps { get; set; } = new List<UpdateDescriptorFile>();

        /// <summary>
        /// Group labels, or null.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Group tags, or null.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;
    }
}
