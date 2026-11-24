namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A published app content version (table contentversions).
    /// </summary>
    public class ContentVersion
    {
        /// <summary>
        /// Identifier (cve_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Monotonic version number.
        /// </summary>
        public long Version { get; set; } = 0;

        /// <summary>
        /// Publish message.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Publish or Restore.
        /// </summary>
        public ContentVersionKindEnum Kind { get; set; } = ContentVersionKindEnum.Publish;

        /// <summary>
        /// Version restored, or null.
        /// </summary>
        public long? RestoredFromVersion { get; set; } = null;

        /// <summary>
        /// Publisher, or null.
        /// </summary>
        public string? PublishedByUserId { get; set; } = null;

        /// <summary>
        /// Publisher display name.
        /// </summary>
        public string PublishedByName { get; set; } = String.Empty;

        /// <summary>
        /// Flags changed.
        /// </summary>
        public int FlagChanges { get; set; } = 0;

        /// <summary>
        /// Remote string changes.
        /// </summary>
        public int StringChanges { get; set; } = 0;

        /// <summary>
        /// Banner changes.
        /// </summary>
        public int BannerChanges { get; set; } = 0;

        /// <summary>
        /// Remote asset changes.
        /// </summary>
        public int AssetChanges { get; set; } = 0;

        /// <summary>
        /// Publish time.
        /// </summary>
        public DateTime PublishedUtc { get; set; } = DateTime.UtcNow;
    }
}
