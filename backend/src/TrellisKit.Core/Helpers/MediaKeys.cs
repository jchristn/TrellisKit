namespace TrellisKit.Core.Helpers
{
    using System;
    using System.Linq;
    using System.Text.RegularExpressions;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Object key layout of the media pipeline (contract sections 4.0 and 4.8). Every output folder is flat: one path
    /// segment per file, so playlists use relative names and the local file route needs one segment per file.
    /// </summary>
    public static class MediaKeys
    {
        #region Public-Members

        /// <summary>
        /// Name of the original object under in/{id}/.
        /// </summary>
        public const string OriginalName = "original";

        /// <summary>
        /// File name of a link preview image under link-previews/{hash}/.
        /// </summary>
        public const string LinkPreviewName = "card.jpg";

        /// <summary>
        /// Variant name of the progressive MP4 download rendition of a lesson video (docs/PHASE4_CONTRACT.md section 5.9).
        /// </summary>
        public const string DownloadVariantName = "download_720p";

        /// <summary>
        /// File name of the progressive MP4 download rendition (in the transcoded location, next to the HLS ladder).
        /// </summary>
        public const string DownloadFileName = "download_720p.mp4";

        #endregion

        #region Private-Members

        private static readonly Regex _FileName = new Regex("^[A-Za-z0-9_-][A-Za-z0-9_.-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _MediaId = new Regex("^med_[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Hash = new Regex("^[A-Za-z0-9_-]{8,128}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// The property prefix every object key starts with, "&lt;propertyId&gt;/" (binding: keys are property-scoped from
        /// the start, so several properties can share storage later). Local folders, S3 keys, CDN paths, and the CloudFront
        /// cookie scope all include it.
        /// </summary>
        /// <param name="propertyId">Property id (app_{k-sortable}_{random}, see PropertyIds).</param>
        /// <returns>Prefix with a trailing slash.</returns>
        /// <exception cref="ArgumentException">The property id is not a valid property id.</exception>
        public static string Prefix(string propertyId)
        {
            if (!PropertyIds.IsValid(propertyId)) throw new ArgumentException("Invalid property id for a media key: " + propertyId + ".", nameof(propertyId));
            return propertyId + "/";
        }

        /// <summary>
        /// Key of the original upload: in/{id}/original (source or video source location).
        /// </summary>
        /// <param name="propertyId">Property id (first key segment; every key is property-scoped).</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <returns>Key.</returns>
        public static string Original(string propertyId, string mediaId)
        {
            return Prefix(propertyId) + "in/" + mediaId + "/" + OriginalName;
        }

        /// <summary>
        /// Prefix of image variants and posters in the images location: media/{id}/.
        /// </summary>
        /// <param name="propertyId">Property id (first key segment; every key is property-scoped).</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <returns>Prefix.</returns>
        public static string ImagePrefix(string propertyId, string mediaId)
        {
            return Prefix(propertyId) + "media/" + mediaId + "/";
        }

        /// <summary>
        /// Prefix of HLS and MP4 outputs in the transcoded location: video/{id}/.
        /// </summary>
        /// <param name="propertyId">Property id (first key segment; every key is property-scoped).</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <returns>Prefix.</returns>
        public static string VideoPrefix(string propertyId, string mediaId)
        {
            return Prefix(propertyId) + "video/" + mediaId + "/";
        }

        /// <summary>
        /// Prefix of public image copies in the images location: public/media/{id}/.
        /// </summary>
        /// <param name="propertyId">Property id (first key segment; every key is property-scoped).</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <returns>Prefix.</returns>
        public static string PublicImagePrefix(string propertyId, string mediaId)
        {
            return Prefix(propertyId) + "public/media/" + mediaId + "/";
        }

        /// <summary>
        /// Prefix of public video copies in the transcoded location: public/video/{id}/.
        /// </summary>
        /// <param name="propertyId">Property id (first key segment; every key is property-scoped).</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <returns>Prefix.</returns>
        public static string PublicVideoPrefix(string propertyId, string mediaId)
        {
            return Prefix(propertyId) + "public/video/" + mediaId + "/";
        }

        /// <summary>
        /// Prefix of retained image outputs in the retained location: removed/{id}/media/.
        /// </summary>
        /// <param name="propertyId">Property id (first key segment; every key is property-scoped).</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <returns>Prefix.</returns>
        public static string RetainedImagePrefix(string propertyId, string mediaId)
        {
            return Prefix(propertyId) + "removed/" + mediaId + "/media/";
        }

        /// <summary>
        /// Prefix of retained video outputs in the retained location: removed/{id}/video/.
        /// </summary>
        /// <param name="propertyId">Property id (first key segment; every key is property-scoped).</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <returns>Prefix.</returns>
        public static string RetainedVideoPrefix(string propertyId, string mediaId)
        {
            return Prefix(propertyId) + "removed/" + mediaId + "/video/";
        }

        /// <summary>
        /// Prefix of everything retained for a media item: removed/{id}/.
        /// </summary>
        /// <param name="propertyId">Property id (first key segment; every key is property-scoped).</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <returns>Prefix.</returns>
        public static string RetainedPrefix(string propertyId, string mediaId)
        {
            return Prefix(propertyId) + "removed/" + mediaId + "/";
        }

        /// <summary>
        /// Prefix of everything stored for a private media item (ticket and direct message attachments) in the source
        /// location, which is never served directly: private/{id}/. The grant cookie paths (media/, video/) never cover it.
        /// </summary>
        /// <param name="propertyId">Property id (first key segment; every key is property-scoped).</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <returns>Prefix.</returns>
        public static string PrivatePrefix(string propertyId, string mediaId)
        {
            return Prefix(propertyId) + "private/" + mediaId + "/";
        }

        /// <summary>
        /// Prefix of a private media item's image variants and posters in the source location: private/{id}/media/.
        /// </summary>
        /// <param name="propertyId">Property id (first key segment; every key is property-scoped).</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <returns>Prefix.</returns>
        public static string PrivateImagePrefix(string propertyId, string mediaId)
        {
            return PrivatePrefix(propertyId, mediaId) + "media/";
        }

        /// <summary>
        /// Prefix of a private media item's HLS and MP4 outputs in the source location: private/{id}/video/.
        /// </summary>
        /// <param name="propertyId">Property id (first key segment; every key is property-scoped).</param>
        /// <param name="mediaId">Media identifier.</param>
        /// <returns>Prefix.</returns>
        public static string PrivateVideoPrefix(string propertyId, string mediaId)
        {
            return PrivatePrefix(propertyId, mediaId) + "video/";
        }

        /// <summary>
        /// Key of an account data export file in the retained location (private, never served directly; downloaded only
        /// through the signed export download route): exports/{exportId}.json.
        /// </summary>
        /// <param name="propertyId">Property id (first key segment; every key is property-scoped).</param>
        /// <param name="exportId">Export identifier (letters, digits, and underscores).</param>
        /// <returns>Key.</returns>
        /// <exception cref="ArgumentException">The export identifier contains other characters.</exception>
        public static string AccountExport(string propertyId, string exportId)
        {
            if (String.IsNullOrEmpty(exportId) || !exportId.All(c => Char.IsAsciiLetterOrDigit(c) || c == '_')) throw new ArgumentException("Invalid export identifier.", nameof(exportId));
            return Prefix(propertyId) + "exports/" + exportId + ".json";
        }

        /// <summary>
        /// Key of a link preview image in the images location: link-previews/{hash}/card.jpg.
        /// </summary>
        /// <param name="propertyId">Property id (first key segment).</param>
        /// <param name="urlHash">URL hash.</param>
        /// <returns>Key.</returns>
        public static string LinkPreview(string propertyId, string urlHash)
        {
            return LinkPreviewFile(propertyId, urlHash, LinkPreviewName);
        }

        /// <summary>
        /// Key of a file in a link preview folder of the images location: link-previews/{hash}/{file}.
        /// </summary>
        /// <param name="propertyId">Property id (first key segment).</param>
        /// <param name="urlHash">URL hash.</param>
        /// <param name="file">File name.</param>
        /// <returns>Key.</returns>
        public static string LinkPreviewFile(string propertyId, string urlHash, string file)
        {
            return Prefix(propertyId) + "link-previews/" + urlHash + "/" + file;
        }

        /// <summary>
        /// Local folder name of a location under storage.directory.
        /// </summary>
        /// <param name="location">Location.</param>
        /// <returns>source, video-source, images, transcoded, or retained.</returns>
        public static string FolderName(MediaLocationEnum location)
        {
            switch (location)
            {
                case MediaLocationEnum.Source:
                    return "source";
                case MediaLocationEnum.VideoSource:
                    return "video-source";
                case MediaLocationEnum.Images:
                    return "images";
                case MediaLocationEnum.Transcoded:
                    return "transcoded";
                default:
                    return "retained";
            }
        }

        /// <summary>
        /// Whether a name is a safe flat output file name (letters, digits, dot, dash, underscore; no leading dot).
        /// </summary>
        /// <param name="fileName">File name.</param>
        /// <returns>True when safe.</returns>
        public static bool IsSafeFileName(string? fileName)
        {
            return !String.IsNullOrEmpty(fileName) && _FileName.IsMatch(fileName) && !fileName.Contains("..", StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether a value looks like a media identifier.
        /// </summary>
        /// <param name="mediaId">Value.</param>
        /// <returns>True when valid.</returns>
        public static bool IsMediaId(string? mediaId)
        {
            return !String.IsNullOrEmpty(mediaId) && _MediaId.IsMatch(mediaId);
        }

        /// <summary>
        /// Whether a value is a safe URL hash segment.
        /// </summary>
        /// <param name="urlHash">Value.</param>
        /// <returns>True when valid.</returns>
        public static bool IsSafeHash(string? urlHash)
        {
            return !String.IsNullOrEmpty(urlHash) && _Hash.IsMatch(urlHash);
        }

        /// <summary>
        /// Content type of a served output file from its extension.
        /// </summary>
        /// <param name="fileName">File name.</param>
        /// <returns>MIME type (application/octet-stream when unknown).</returns>
        public static string ContentTypeFor(string fileName)
        {
            string lower = (fileName ?? String.Empty).ToLowerInvariant();
            if (lower.EndsWith(".webp", StringComparison.Ordinal)) return "image/webp";
            if (lower.EndsWith(".jpg", StringComparison.Ordinal) || lower.EndsWith(".jpeg", StringComparison.Ordinal)) return "image/jpeg";
            if (lower.EndsWith(".png", StringComparison.Ordinal)) return "image/png";
            if (lower.EndsWith(".m3u8", StringComparison.Ordinal)) return "application/vnd.apple.mpegurl";
            if (lower.EndsWith(".ts", StringComparison.Ordinal)) return "video/mp2t";
            if (lower.EndsWith(".mp4", StringComparison.Ordinal)) return "video/mp4";
            if (lower.EndsWith(".m4s", StringComparison.Ordinal)) return "video/iso.segment";
            if (lower.EndsWith(".pdf", StringComparison.Ordinal)) return "application/pdf";
            if (lower.EndsWith(".vtt", StringComparison.Ordinal)) return "text/vtt";
            return "application/octet-stream";
        }

        #endregion
    }
}
