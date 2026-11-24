namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// ContentVersion payload.
    /// </summary>
    public class ContentVersionView
    {
        /// <summary>
        /// Identifier (cve_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Version.
        /// </summary>
        public long Version { get; set; } = 0;

        /// <summary>
        /// Message.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Publish or Restore.
        /// </summary>
        public ContentVersionKindEnum Kind { get; set; } = ContentVersionKindEnum.Publish;

        /// <summary>
        /// Restored version, or null.
        /// </summary>
        public long? RestoredFromVersion { get; set; } = null;

        /// <summary>
        /// Publisher.
        /// </summary>
        public string PublishedBy { get; set; } = String.Empty;

        /// <summary>
        /// Publish time.
        /// </summary>
        public DateTime PublishedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Changes by object type (flags, strings, banners, assets).
        /// </summary>
        public Dictionary<string, int> ChangeCounts { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// Changed flags preview (detail only), or null.
        /// </summary>
        public FlagHistoryPreview? Changes { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Same as publishedUtc.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
