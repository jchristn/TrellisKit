namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One platform's live update (table updates).
    /// </summary>
    public class UpdateRecord
    {
        /// <summary>
        /// Owning property.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Identifier (upd_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Owning group.
        /// </summary>
        public string GroupId { get; set; } = String.Empty;

        /// <summary>
        /// Expo manifest id (UUID v4).
        /// </summary>
        public string Uuid { get; set; } = String.Empty;

        /// <summary>
        /// ios or android.
        /// </summary>
        public string Platform { get; set; } = String.Empty;

        /// <summary>
        /// Fingerprint runtime version.
        /// </summary>
        public string RuntimeVersion { get; set; } = String.Empty;

        /// <summary>
        /// Channel (copied from the group).
        /// </summary>
        public string Channel { get; set; } = String.Empty;

        /// <summary>
        /// Kind (copied from the group).
        /// </summary>
        public UpdateGroupKindEnum Kind { get; set; } = UpdateGroupKindEnum.Publish;

        /// <summary>
        /// Status (copied from the group).
        /// </summary>
        public UpdateGroupStatusEnum Status { get; set; } = UpdateGroupStatusEnum.Draft;

        /// <summary>
        /// Rollout in basis points for this platform.
        /// </summary>
        public int RolloutBasisPoints { get; set; } = 0;

        /// <summary>
        /// Manifest createdAt (millisecond precision, strictly increasing per target).
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// SHA-256 hex of the launch asset, or null for rollback-to-embedded records.
        /// </summary>
        public string? LaunchAssetSha256 { get; set; } = null;

        /// <summary>
        /// Launch asset size.
        /// </summary>
        public long LaunchAssetSizeBytes { get; set; } = 0;

        /// <summary>
        /// Assets besides the launch asset.
        /// </summary>
        public int AssetCount { get; set; } = 0;

        /// <summary>
        /// Bytes of the launch asset and every asset.
        /// </summary>
        public long TotalBytes { get; set; } = 0;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last change time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
