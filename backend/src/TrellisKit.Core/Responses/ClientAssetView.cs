namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A remote asset descriptor as clients receive it.
    /// </summary>
    public class ClientAssetView
    {
        /// <summary>
        /// Asset id.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = "image/webp";

        /// <summary>
        /// Size.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Width.
        /// </summary>
        public int? Width { get; set; } = null;

        /// <summary>
        /// Height.
        /// </summary>
        public int? Height { get; set; } = null;

        /// <summary>
        /// SHA-256 hex.
        /// </summary>
        public string Sha256 { get; set; } = String.Empty;

        /// <summary>
        /// Absolute URL.
        /// </summary>
        public string Url { get; set; } = String.Empty;

        /// <summary>
        /// Alternative text.
        /// </summary>
        public string? AltText { get; set; } = null;

        /// <summary>
        /// Dark variant.
        /// </summary>
        public RemoteAssetVariantView? DarkVariant { get; set; } = null;

        /// <summary>
        /// Content version of the revision.
        /// </summary>
        public long ContentVersion { get; set; } = 0;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
