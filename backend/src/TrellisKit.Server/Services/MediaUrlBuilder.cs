namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Builds media URLs and the Media payload (contract section 2). With media.publicBaseUrl set, URLs are absolute
    /// ({base}/media/{id}/{file}, {base}/video/{id}/{file}, public copies under {base}/public/media/{id}/ and
    /// {base}/public/video/{id}/, link previews under {base}/link-previews/{hash}/card.jpg). With it empty (Local),
    /// URLs are root-relative: /api/v1/media/files/{id}/{file}, public copies /api/v1/media/files/public/{id}/{file},
    /// and link previews /api/v1/media/files/link-previews/{hash}/card.jpg.
    /// Thread safety: stateless apart from live settings; safe to call concurrently.
    /// </summary>
    public class MediaUrlBuilder
    {
        #region Public-Members

        /// <summary>
        /// Root of the local file route.
        /// </summary>
        public const string FilesPath = Constants.ApiPrefix + "/media/files";

        /// <summary>
        /// Variant name of the served file of a Document media (file.pdf or file.vtt in the images location).
        /// </summary>
        public const string DocumentVariantName = "file";

        /// <summary>
        /// Variant names with their long edges, in order.
        /// </summary>
        public static readonly IReadOnlyList<KeyValuePair<string, int>> VariantSizes = new List<KeyValuePair<string, int>>
        {
            new KeyValuePair<string, int>("thumb", 320),
            new KeyValuePair<string, int>("medium", 1080),
            new KeyValuePair<string, int>("large", 2048)
        };

        #endregion

        #region Private-Members

        private readonly AppSettings _Settings;
        private readonly string _PropertyId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the builder.
        /// </summary>
        /// <param name="settings">Running settings (media.publicBaseUrl is read live).</param>
        /// <param name="propertyId">Property id: the first segment of every object key.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MediaUrlBuilder(AppSettings settings, string propertyId)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _PropertyId = propertyId ?? throw new ArgumentNullException(nameof(propertyId));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// URL of an image-location file (variant or poster) of a media item.
        /// </summary>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="file">Flat file name.</param>
        /// <param name="publicPath">Use the public copy path.</param>
        /// <returns>URL.</returns>
        public string ImageUrl(string mediaId, string file, bool publicPath)
        {
            string baseUrl = _Settings.Media.PublicBaseUrl.TrimEnd('/');
            if (baseUrl.Length > 0) return baseUrl + "/" + (publicPath ? MediaKeys.PublicImagePrefix(_PropertyId, mediaId) : MediaKeys.ImagePrefix(_PropertyId, mediaId)) + file;
            return FilesPath + (publicPath ? "/public/" : "/") + mediaId + "/" + file;
        }

        /// <summary>
        /// URL of a transcoded-location file (playlist, segment, MP4) of a media item.
        /// </summary>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="file">Flat file name.</param>
        /// <param name="publicPath">Use the public copy path.</param>
        /// <returns>URL.</returns>
        public string VideoUrl(string mediaId, string file, bool publicPath)
        {
            string baseUrl = _Settings.Media.PublicBaseUrl.TrimEnd('/');
            if (baseUrl.Length > 0) return baseUrl + "/" + (publicPath ? MediaKeys.PublicVideoPrefix(_PropertyId, mediaId) : MediaKeys.VideoPrefix(_PropertyId, mediaId)) + file;
            return FilesPath + (publicPath ? "/public/" : "/") + mediaId + "/" + file;
        }

        /// <summary>
        /// URL of a stored link preview image.
        /// </summary>
        /// <param name="key">Key (link-previews/{hash}/card.jpg), or null.</param>
        /// <returns>URL, or null.</returns>
        public string? LinkPreviewUrl(string? key)
        {
            if (String.IsNullOrEmpty(key)) return null;
            string baseUrl = _Settings.Media.PublicBaseUrl.TrimEnd('/');
            if (baseUrl.Length > 0) return baseUrl + "/" + key;
            string prefix = MediaKeys.Prefix(_PropertyId);
            // The local file route resolves the property prefix itself, so local URLs keep the contract's path shape.
            return FilesPath + "/" + (key.StartsWith(prefix, StringComparison.Ordinal) ? key.Substring(prefix.Length) : key);
        }

        /// <summary>
        /// Build the Media payload. Image and video URLs appear only when the media is Ready and not retained.
        /// </summary>
        /// <param name="media">Media.</param>
        /// <param name="variants">Its variants, or null.</param>
        /// <param name="publicPaths">Use the public copy paths (only when the media was copied there).</param>
        /// <param name="metadata">Labels and tags, or null for none.</param>
        /// <returns>Payload.</returns>
        /// <exception cref="ArgumentNullException">The media is null.</exception>
        public MediaView Build(MediaAttachment media, List<MediaVariant>? variants, bool publicPaths, ObjectMetadata? metadata)
        {
            ArgumentNullException.ThrowIfNull(media);
            bool usePublic = publicPaths && media.IsPublic;
            if (media.IsPrivate) return BuildCore(media, variants, metadata, null, null);
            MediaView view = BuildCore(media, variants, metadata, file => ImageUrl(media.Id, file, usePublic), file => VideoUrl(media.Id, file, usePublic));
            return publicPaths ? ForPublic(view) : view;
        }

        /// <summary>
        /// Trim a Media payload for an anonymous or public response: the uploader (ownerUserId) and any failure reason are
        /// removed, so public routes never name the staff member or member who uploaded the file.
        /// </summary>
        /// <param name="view">Payload (changed in place).</param>
        /// <returns>The same payload.</returns>
        /// <exception cref="ArgumentNullException">The payload is null.</exception>
        public static MediaView ForPublic(MediaView view)
        {
            ArgumentNullException.ThrowIfNull(view);
            view.OwnerUserId = null;
            view.FailureReason = null;
            return view;
        }

        /// <summary>
        /// Build the Media payload of a private media item (ticket or direct message attachment) with per-object URLs: every
        /// file URL comes from fileUrl (the private file route with a short-lived token for one viewer). Image and video URLs
        /// appear only when the media is Ready and not retained.
        /// </summary>
        /// <param name="media">Media.</param>
        /// <param name="variants">Its variants, or null.</param>
        /// <param name="metadata">Labels and tags, or null for none.</param>
        /// <param name="fileUrl">URL of one flat output file name.</param>
        /// <returns>Payload.</returns>
        /// <exception cref="ArgumentNullException">The media or the URL function is null.</exception>
        public MediaView Build(MediaAttachment media, List<MediaVariant>? variants, ObjectMetadata? metadata, Func<string, string> fileUrl)
        {
            ArgumentNullException.ThrowIfNull(media);
            ArgumentNullException.ThrowIfNull(fileUrl);
            return BuildCore(media, variants, metadata, fileUrl, fileUrl);
        }

        #endregion

        #region Private-Methods

        private MediaView BuildCore(MediaAttachment media, List<MediaVariant>? variants, ObjectMetadata? metadata, Func<string, string>? imageUrl, Func<string, string>? videoUrl)
        {
            MediaView view = new MediaView
            {
                Id = media.Id,
                Kind = media.Kind,
                Status = media.Status,
                FailureReason = media.Status == MediaStatusEnum.Failed ? media.FailureReason : null,
                OwnerUserId = media.OwnerUserId,
                ContentType = media.ContentType,
                SizeBytes = media.SizeBytes,
                Width = media.Status == MediaStatusEnum.Ready ? media.Width : null,
                Height = media.Status == MediaStatusEnum.Ready ? media.Height : null,
                DurationSeconds = media.Kind == MediaKindEnum.Video ? media.DurationSeconds : null,
                AltText = media.AltText,
                Blurhash = media.Status == MediaStatusEnum.Ready ? media.Blurhash : null,
                CreatedUtc = media.CreatedUtc,
                ReadyUtc = media.ReadyUtc,
                Labels = metadata?.Labels ?? new List<string>(),
                Tags = metadata?.Tags ?? new Dictionary<string, string>()
            };
            if (media.Status != MediaStatusEnum.Ready || media.IsRetained || imageUrl == null || videoUrl == null) return view;
            if (media.Kind == MediaKindEnum.Image)
            {
                MediaImageView image = new MediaImageView();
                List<MediaVariant> list = variants ?? new List<MediaVariant>();
                foreach (KeyValuePair<string, int> size in VariantSizes)
                {
                    MediaVariant? webp = list.FirstOrDefault(v => v.Name == size.Key && v.Format == "webp");
                    MediaVariant? jpeg = list.FirstOrDefault(v => v.Name == size.Key && v.Format == "jpeg");
                    MediaVariant? any = webp ?? jpeg;
                    if (any == null) continue;
                    image.Variants[size.Key] = new MediaVariantView
                    {
                        Width = any.Width,
                        Height = any.Height,
                        WebpUrl = webp == null ? null : imageUrl(webp.FileName),
                        JpegUrl = jpeg == null ? null : imageUrl(jpeg.FileName)
                    };
                }
                view.Image = image;
            }
            else if (media.Kind == MediaKindEnum.Document)
            {
                MediaVariant? file = (variants ?? new List<MediaVariant>()).FirstOrDefault(v => v.Name == DocumentVariantName);
                view.Document = new MediaDocumentView { Url = file == null ? null : imageUrl(file.FileName), FileName = media.OriginalFileName };
            }
            else
            {
                view.Video = new MediaVideoView
                {
                    HlsUrl = media.HlsManifestFile == null ? null : videoUrl(media.HlsManifestFile),
                    Mp4Url = media.Mp4File == null ? null : videoUrl(media.Mp4File),
                    PosterUrl = media.PosterFile == null ? null : imageUrl(media.PosterFile),
                    PosterThumbUrl = media.PosterThumbFile == null ? null : imageUrl(media.PosterThumbFile)
                };
            }
            return view;
        }

        #endregion
    }
}
