namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One platform's update inside an UpdateGroup payload.
    /// </summary>
    public class UpdateView
    {
        /// <summary>
        /// Identifier (upd_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Expo manifest id.
        /// </summary>
        public string Uuid { get; set; } = String.Empty;

        /// <summary>
        /// ios or android.
        /// </summary>
        public string Platform { get; set; } = String.Empty;

        /// <summary>
        /// Runtime version.
        /// </summary>
        public string RuntimeVersion { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public UpdateGroupStatusEnum Status { get; set; } = UpdateGroupStatusEnum.Draft;

        /// <summary>
        /// Manifest createdAt.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Launch asset size.
        /// </summary>
        public long LaunchAssetSizeBytes { get; set; } = 0;

        /// <summary>
        /// Assets besides the launch asset.
        /// </summary>
        public int AssetCount { get; set; } = 0;

        /// <summary>
        /// Total bytes.
        /// </summary>
        public long TotalBytes { get; set; } = 0;

        /// <summary>
        /// Rollout percentage of this platform.
        /// </summary>
        public decimal RolloutPercentage { get; set; } = 0m;

        /// <summary>
        /// Private source maps stored (detail only).
        /// </summary>
        public long? SourceMapCount { get; set; } = null;
    }
}
