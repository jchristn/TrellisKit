namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A banner as clients receive it in the app manifest (no targeting rules).
    /// </summary>
    public class ClientBannerView
    {
        /// <summary>
        /// Banner id.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Markdown body.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Style.
        /// </summary>
        public string Style { get; set; } = "Info";

        /// <summary>
        /// Placement.
        /// </summary>
        public string Placement { get; set; } = "Global";

        /// <summary>
        /// Whether members may dismiss it.
        /// </summary>
        public bool Dismissible { get; set; } = true;

        /// <summary>
        /// Priority.
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// Schedule start.
        /// </summary>
        public DateTime? StartsUtc { get; set; } = null;

        /// <summary>
        /// Schedule end.
        /// </summary>
        public DateTime? EndsUtc { get; set; } = null;

        /// <summary>
        /// Link.
        /// </summary>
        public BannerLinkView? Link { get; set; } = null;

        /// <summary>
        /// Image asset id.
        /// </summary>
        public string? ImageAssetId { get; set; } = null;

        /// <summary>
        /// Hash of the title and body.
        /// </summary>
        public string ContentHash { get; set; } = String.Empty;

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
