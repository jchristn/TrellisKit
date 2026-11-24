namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A cached link preview (contract section 5.9). Successful rows are never modified after insert, so content that
    /// references one keeps its card; a refresh inserts a new row and moves the old one off the unique url hash.
    /// </summary>
    public class LinkPreview
    {
        #region Public-Members

        /// <summary>
        /// Identifier (lpv_ prefix).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// SHA-256 (hex) of the normalized URL; "old:" plus the id for a row replaced by a refresh.
        /// </summary>
        public string UrlHash { get; set; } = String.Empty;

        /// <summary>
        /// Normalized URL.
        /// </summary>
        public string Url { get; set; } = String.Empty;

        /// <summary>
        /// Canonical URL (og:url, the canonical link, or the final URL after redirects), or null.
        /// </summary>
        public string? CanonicalUrl { get; set; } = null;

        /// <summary>
        /// Site name, or null.
        /// </summary>
        public string? SiteName { get; set; } = null;

        /// <summary>
        /// Title (at most 200 characters), or null.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Description (at most 300 characters), or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Stored image key, or null.
        /// </summary>
        public string? ImageKey { get; set; } = null;

        /// <summary>
        /// Stored image width, or null.
        /// </summary>
        public int? ImageWidth { get; set; } = null;

        /// <summary>
        /// Stored image height, or null.
        /// </summary>
        public int? ImageHeight { get; set; } = null;

        /// <summary>
        /// Ok or Failed.
        /// </summary>
        public LinkPreviewStatusEnum Status { get; set; } = LinkPreviewStatusEnum.Ok;

        /// <summary>
        /// Failure reason for Failed rows, or null.
        /// </summary>
        public string? FailureReason { get; set; } = null;

        /// <summary>
        /// When the page was fetched (UTC).
        /// </summary>
        public DateTime FetchedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the cache entry expires (UTC).
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
